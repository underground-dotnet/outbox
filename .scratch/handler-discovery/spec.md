# Handler discovery replaces manual registration

Status: ready-for-agent

## Problem Statement

A handler has to be declared twice, and nothing checks that the two declarations agree.

The source generator already finds every handler — locally through the syntax tree, and across
assemblies marked `[assembly: ContainsOutboxHandlers]` through metadata — but it only emits the
dispatcher. Putting the handler into the container is a separate, manual act: `cfg.AddHandler<TH,
TM>()`. When the two lists disagree, the generator dispatches to a service that was never
registered and `GetRequiredService` throws at run time, on a message that has already been claimed.

This is not hypothetical. `example/MultiProjectApp` ships with it: `DemoHandler` implements
`IOutboxMessageHandler<DemoMessage>` and is discovered and dispatched, but `Program.cs` never
registers it. A `DemoMessage` would fail on every attempt until the message exhausted its group's
patience. Nothing in the build reports it.

The cross-assembly half has its own problems. Scanning the namespace tree of every referenced
assembly for types that implement an interface is the pattern the Roslyn incremental-generator
cookbook names as an anti-pattern — "very expensive, and cannot be done incrementally" — because it
must fetch `AllInterfaces` on every type in every reference. The implementation also cannot resolve
generic type arguments: it builds a throwaway `CSharpCompilation` holding a single reference, so
`Underground.Outbox` itself is absent and the handler interfaces come back as error types. That is
why `OutboxGenerator` matches the *string* `"Underground.Outbox.IOutboxMessageHandler<"` rather than
comparing symbols. Renaming the namespace would silently disable cross-assembly discovery.

Finally, the dispatcher is generated as a single type with a global view of every handler, and the
DI entry points are emitted from a constant string into a fixed namespace. Both assume exactly one
project runs the generator. That assumption is undocumented outside one line of the README, and it
is the same assumption whose violation produces the long-running CS0436 collision reports against
comparable libraries.

## Solution

Discovery becomes the only way a handler is registered, and discovery becomes local to the assembly
that declares the handler.

Every project containing handlers runs the generator, and the generator emits one method for that
project — `Add<Assembly>MessageHandlers()` — which registers its own handlers and contributes one
**Handler Entry** per handled message type. The composition root calls one such method per module.
Cross-assembly metadata scanning, the `[ContainsOutboxHandlers]` marker, and the string match all
disappear; discovery reads only the syntax of the assembly being compiled.

The generated dispatcher goes with them. In its place the library owns a **Handler Registry**: the
startup-built map from a message's stored `Type` to the one handler that may run it. A Handler Entry
carries a delegate that closes over the typed `JsonSerializer.Deserialize<TMessage>` and
`GetRequiredService<IOutboxMessageHandler<TMessage>>` call sites, so dispatch stays free of
reflection and the linear `if`-chain becomes a dictionary lookup. Two entries claiming the same
message type are a contradiction rather than a preference, so building the registry throws.

Handler lifetime moves onto the handler, where it belongs, as an optional
`[MessageHandlerLifetime]`. Absent, a handler is Transient, as it is today. Because a handler
implementing several message interfaces is now registered once as its concrete type with each
interface forwarding to it, that lifetime finally means what it says: one Scoped handler is one
instance per scope, not one per message type it handles.

Exception policies do not move. They are attached per `(handler, message type)` pair, they differ
between compositions of the same handler, and `ExceptionPolicy<TEntity>` is an open abstraction
whose implementations are resolved from the container — none of which an attribute can express. The
chain survives unchanged; only its entry point is renamed, from `AddHandler` (which registered a
handler) to `ForHandler` (which configures a discovered one).

## What this costs

Two handlers in *different* assemblies claiming the same message type can no longer be caught at
compile time, because no single compilation sees both. Within one assembly the generator still
reports it as an error. Across assemblies it becomes a throw when the host starts, before the first
message is claimed. This is a real reduction in guarantee and the main reason this design is worth
an ADR.

A handler in an assembly that does not run the generator is silently not registered. The failure
surfaces as the existing "no handler configured for message type" `ParsingException` at dispatch —
the same class of failure as today's `DemoHandler`, but now it is the only one left, and it is
caused by a missing package reference rather than by a forgotten line of configuration.

## User Stories

1. As an application developer, I want a handler I have written to be registered by virtue of implementing the handler interface, so that adding a handler is one act rather than two that can disagree.
2. As an application developer, I want a handler that is dispatched but not registered to be impossible, so that I cannot ship the failure `DemoHandler` demonstrates.
3. As an application developer, I want to control a handler's DI lifetime on the handler itself, so that the decision lives next to the code whose lifetime it governs.
4. As an application developer, I want the lifetime to be optional and to default to Transient, so that handlers that do not care say nothing.
5. As an application developer with a handler that handles several message types, I want one Scoped instance per scope rather than one per message type, so that the lifetime means what its name says.
6. As an application developer in a modular solution, I want each module to contribute its own handlers through one call, so that the composition root lists modules rather than handlers.
7. As an application developer, I want two handlers claiming the same message type to stop the application at startup, so that an ambiguity I cannot have intended never decides itself silently.
8. As an application developer, I want that ambiguity reported at compile time when both handlers are in one assembly, so that the fast feedback is not lost where it is still available.
9. As an application developer, I want to keep attaching exception policies per handler and message type, including different policies for the same handler in different compositions, so that nothing I can express today becomes inexpressible.
10. As a library maintainer, I want handler discovery to read only the compiling assembly's syntax, so that build and IDE performance does not degrade with the size of the dependency graph.
11. As a library maintainer, I want the generated code to contain no reflection over handler or message types, so that `IsAotCompatible` becomes reachable.
12. As an application developer, I want the order of my `AddOutboxServices` and `Add<Assembly>MessageHandlers` calls not to matter, so that there is no rule to get wrong.

## Decisions

Settled in the design session that produced this spec. Each records what was chosen and the
alternative it beat.

| # | Decision | Rejected alternative |
| --- | --- | --- |
| 1 | Discovery is the only registration path; `AddHandler` is deleted | Keeping it as an override, or as an opt-out |
| 2 | Exception policies stay in configuration, via `ForHandler<TH, TM>()` | Moving them to attributes on the handler |
| 3 | Default lifetime stays `Transient` | Changing the default to `Scoped` in the same release |
| 4 | Handlers register as their concrete type; each interface forwards to it | Keeping one independent registration per interface |
| 5 | Per-assembly generation; no cross-assembly metadata scanning | Extending the existing `[ContainsOutboxHandlers]` scan to emit registrations |
| 6 | The lifetime attribute is optional; discovery is by interface | A mandatory marker attribute, enabling `ForAttributeWithMetadataName` |
| 7 | One Handler Registry keyed by `message.Type`, fed by every assembly | One dispatcher fragment per assembly, tried in turn |
| 8 | Duplicate message types throw when the host starts | Last-wins, or first-wins with a warning |
| 9 | `AddOutboxServices` / `AddInboxServices` move into the library | Keeping them as generator post-initialization output |
| 10 | Validation runs in `BackgroundService<TEntity>.StartAsync` | Validating inside `AddOutboxServices`, which would impose a call order |
| 11 | `[MessageHandlerLifetime(ServiceLifetime)]`, falling back to Transient | Adding a caller-supplied default to the generated method |
| 12 | One `Add<Assembly>MessageHandlers()` covering both sides | Separate inbox and outbox methods per assembly |
| 13 | Entries carry typed delegates | Entries carry `Type` values for the library to reflect over |
| 14 | `ForHandler` naming an undiscovered handler throws at host start | Logging a warning and continuing |

## Invariants the change must not break

- A message's `Type` is written from the runtime type and read by the dispatcher. Nested
  (`Outer+Inner`) and generic (``Wrapped`1[[...]]``) spellings must continue to match. This is what
  `MessageTypeNameTests` exists to pin, and the Handler Registry must be keyed on the same
  `typeof(T).FullName` expression rather than on a compile-time literal.
- A handler's exception is wrapped in `MessageHandlerException` carrying `handler.GetType()` and
  `typeof(TMessage)`. `ProcessExceptionFromHandler` selects policies on exactly those two values;
  whatever the generator emits must keep the wrapping.
- An unknown message type throws `ParsingException`.
- `OperationCanceledException` is not wrapped.
