# 04: The Handler Registry and the library-owned dispatcher

**What to build:** The runtime half of 03. The library gains a Handler Registry — the startup-built
map from a message's stored `Type` to the one handler that may run it — and an `IMessageDispatcher`
implementation that looks up an entry and invokes its delegate.

The registry is built from the Handler Entries every generated method contributed to the container,
so it does not care which assembly supplied which entry, and it does not care in what order
`AddOutboxServices` and the generated methods were called. That order-independence is the reason
validation happens at host start rather than inside `AddOutboxServices`.

Two entries claiming one message type is a contradiction, not a preference: the registry refuses to
build. Within a single assembly OUTBOX001 catches this at compile time; across assemblies this throw
is the only thing that can.

**Blocked by:** 03 (nothing contributes entries until the generator emits them).

**Status:** ready-for-agent

- [ ] A Handler Registry type in `Underground.Outbox` builds a map from message type name to Handler Entry, separately per message side.
- [ ] Building the registry throws when two entries claim the same message type on the same side, and the exception names the message type and both handlers.
- [ ] The registry is built and validated in `BackgroundService<TEntity>.StartAsync`, before any message is claimed, so that the order of registration calls does not matter.
- [ ] `IMessageDispatcher<TMessage>` is implemented in the library: it looks the message's `Type` up in the registry and invokes the entry's delegate.
- [ ] A message whose type is absent from the registry throws `ParsingException`, with the message it throws today.
- [ ] A message whose body will not deserialize throws `ParsingException`, with the message it throws today.
- [ ] The dispatcher registration inside `AddOutboxServices` / `AddInboxServices` points at the library implementation.
- [ ] `ProcessExceptionFromHandler` still selects policies correctly, because the entry delegate preserves the `MessageHandlerException` wrapping.
