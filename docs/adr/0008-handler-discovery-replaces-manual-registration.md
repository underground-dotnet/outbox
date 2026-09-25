# Handlers are discovered, and each assembly registers its own

A handler used to be declared twice. The source generator found it and built a dispatcher around it;
`AddHandler<TH, TM>()` put it in the container. Nothing checked that the two agreed. When they
disagreed the generator dispatched to a service nobody had registered, and `GetRequiredService` threw
on a message that had already been claimed — a failure that survives a restart and blocks its Group,
reported nowhere at build time. `example/MultiProjectApp` shipped with an instance of it.

Implementing a handler interface is now the only thing that registers a handler. `AddHandler` is
gone.

Discovery is also now local. Every project that declares handlers runs the generator and emits
`Add<Assembly>MessageHandlers()`; the composition root calls one per module. The previous design had
a single compilation scan the namespace tree of every assembly marked `[ContainsOutboxHandlers]`,
which is the pattern the Roslyn incremental-generator cookbook names as an anti-pattern: it fetches
`AllInterfaces` on every type in every reference and cannot be cached incrementally. That scan could
not resolve generic type arguments either, because it built a throwaway `CSharpCompilation` holding
one reference and therefore without `Underground.Outbox` in it; the code compensated by matching the
*string* `"Underground.Outbox.IOutboxMessageHandler<"`, which a namespace rename would have broken
silently. Discovery now reads the compiling assembly's syntax and compares interface symbols.

The generated dispatcher is replaced by the **Handler Registry**, built from the **Handler Entries**
every module contributed to the container. Entries carry a delegate closing over the typed
`JsonSerializer.Deserialize<TMessage>` and `GetRequiredService<...>` call sites, so dispatch does no
reflection and the linear `if`-chain becomes a dictionary lookup. Because the registry is assembled
from the container rather than from one compilation, the order in which the generated methods and
`AddOutboxServices` are called does not matter.

Handler lifetime moved onto the handler as an optional `[MessageHandlerLifetime]`, defaulting to
`Transient` as hand-written registration did. A handler is registered once as its concrete type with
each handler interface forwarding to it, so `Scoped` now means one instance per scope rather than one
per message type the handler happens to handle.

Exception policies deliberately did **not** move to attributes. They are attached per (handler,
message type) pair — `FailedMultipleMessagesHandler` carries one on one of its two message types and
none on the other — they differ between compositions of the same handler, and `ExceptionPolicy<T>` is
an open abstraction whose implementations are resolved from the container. An attribute expresses
none of that. `AddHandler`'s policy chain survives verbatim under the name `ForHandler`, which
configures a handler discovery already registered.

## Consequences

**Two handlers in different assemblies claiming one message type is no longer a compile-time error.**
No single compilation sees both, so nothing can report it at build time. Building the registry throws
instead, as the host starts and before any message is claimed. Within one assembly OUTBOX001 still
reports it as an error. This is a real reduction in guarantee and the main cost of the change; it
buys back the incremental-generator behaviour and the per-assembly composition that made the rest
possible. Comparable libraries pay the same price and pay it worse — the closest analogue reports
duplicates as a *warning* and silently drops the loser.

**A handler in a project that does not run the generator is silently not registered.** It surfaces as
the existing "no handler configured for message type" `ParsingException` at dispatch. This is the
same shape as the bug the change removes, but its cause is a missing package reference rather than a
forgotten line of configuration, and it is now the only such case left.

**The generator belongs in every project that declares handlers**, reversing the README's previous
instruction to add it only to the root project. That instruction existed because the generated DI
entry points sat in a fixed namespace and would have collided; those are ordinary library source now,
and the only generated type is named after its assembly.

**`ForHandler` is a rename, not a redesign.** Existing `AddHandler` chains that attached a policy port
by changing the verb; those that only registered are deleted outright.
