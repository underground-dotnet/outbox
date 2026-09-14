# 03: Per-assembly generated registration, and the end of cross-assembly scanning

**What to build:** The generator stops emitting one dispatcher with a global view and starts
emitting, for each compilation that contains handlers, a single method registering that assembly's
handlers and contributing its Handler Entries.

Discovery becomes local: the syntax provider over the compiling assembly only. `ScanReferencedAssembly`,
`ScanNamespaceForHandlers`, the `MetadataReferencesProvider` pipeline, the throwaway
`CSharpCompilation`, and the `"Underground.Outbox.IOutboxMessageHandler<"` string match are all
deleted. Because the real compilation's semantic model is now the only source, handler interfaces
can be compared as symbols rather than as display-string prefixes.

A handler is registered once as its concrete type at its lifetime, and each handler interface it
implements is registered as a forwarding factory resolving that concrete type. This is what makes
`[MessageHandlerLifetime(Scoped)]` on a handler of several message types mean one instance per
scope.

**Blocked by:** 02 (the attribute must exist to be read).

**Status:** ready-for-agent

- [ ] The generator emits one `Add<Identifier>MessageHandlers(this IServiceCollection)` per compilation that contains at least one handler, and emits nothing for a compilation that contains none.
- [ ] `<Identifier>` derives from the assembly name with `.`, `-` and spaces removed.
- [ ] The method registers every discovered handler's concrete type at its lifetime, and registers each implemented handler interface as a factory resolving that concrete type.
- [ ] Lifetime is read from `[MessageHandlerLifetime]` when present and is `ServiceLifetime.Transient` otherwise.
- [ ] The method contributes one Handler Entry per (handler, message type) pair, each carrying the message type's `typeof(T).FullName` evaluated at run time — never a compile-time string literal — and a delegate that deserializes to the concrete message type and resolves the concrete handler interface, with no reflection over either.
- [ ] The delegate wraps a handler exception in `MessageHandlerException` carrying `handler.GetType()` and `typeof(TMessage)`, and does not wrap `OperationCanceledException`.
- [ ] Abstract handler classes are still ignored; non-handler classes are still ignored.
- [ ] OUTBOX001 still reports two handlers claiming one message type *within* the compiling assembly, as an error.
- [ ] All cross-assembly scanning code and the generated dispatcher are deleted.
- [ ] `example/MultiProjectApp` and `example/MultiProjectLib` both reference the generator, and `MultiProjectApp` composes both modules' generated methods.
- [ ] Snapshot tests cover: a local outbox handler; local inbox and outbox handlers together; a nested message type; a generic message type; a handler carrying an explicit lifetime; a handler implementing two message interfaces; and an assembly with no handlers producing no method.
