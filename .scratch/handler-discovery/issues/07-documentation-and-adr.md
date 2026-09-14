# 07: README, CONTEXT.md and ADR 0008

**What to build:** The written record. This lands last because it describes what the other tickets
actually did, but it is not optional: the change reverses a documented instruction and downgrades a
compile-time guarantee, and both need to be findable by someone who was not in the room.

**Blocked by:** 06.

**Status:** done

- [x] `docs/adr/0008-handler-discovery-replaces-manual-registration.md` records the decision, with its own section on the cross-assembly OUTBOX001 downgrade from compile-time error to startup throw, and names the rejected alternative (extending cross-assembly metadata scanning to emit registrations).
- [x] The ADR notes that the existing duplicate `0006-` numbering is left alone deliberately rather than renumbered.
- [x] `CONTEXT.md` gains **Handler Lifetime** and **Handler Registry** (with **Handler Entry**), each with its `_Avoid_` line; *Scope* stays reserved for `IServiceScope`.
- [x] The README registration section shows handlers being discovered, with `ForHandler` used only where a policy is attached.
- [x] The README instruction to add the generator only to the root project is replaced: the generator belongs in every project that declares handlers, and the composition root calls one generated method per module.
- [x] The README documents `[MessageHandlerLifetime]`, its Transient default, and that it governs the concrete handler rather than one of its message types.
- [x] The README documents the startup throw on two handlers claiming one message type, and that a handler in a project without the generator is simply not registered.
- [x] `package.readme.md` is checked for the same claims.
