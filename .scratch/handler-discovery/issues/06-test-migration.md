# 06: Migrate the test suite

**What to build:** The suite stops registering handlers by hand and starts relying on discovery,
and the tests that reach into the generated dispatcher are ported to the registry.

Port `MessageTypeNameTests` first, not last. Its three tests pin the nested (`Outer+Inner`) and
generic (``Wrapped`1[[...]]``) `Type.FullName` round-trip — the exact invariant the Handler Registry
has to preserve — and they currently do it by constructing `new GeneratedDispatcher<OutboxMessage>()`
directly against a hand-built `ServiceCollection`. That type no longer exists, so these are the
tests most likely to be quietly weakened during the port. They should end up exercising the registry
through the same public path production uses.

The other ~30 call sites are mechanical: the 14 handlers in `test/Underground.OutboxTest/TestHandler`
all declare distinct message types, so discovery registering every one of them in every test does not
trip OUTBOX001 and does not change which handler receives which message.

**Blocked by:** 05.

**Status:** done

- [x] `MessageTypeNameTests` exercises dispatch through the Handler Registry rather than a directly constructed dispatcher, and still asserts the nested and generic `Type` spellings and the `ParsingException` on an unknown type.
- [x] Every `cfg.AddHandler<...>()` call site is removed; those that only registered become nothing, and those that attached a policy become `cfg.ForHandler<...>()`.
- [x] `ProcessorScopeTests` covers lifetime through `[MessageHandlerLifetime(ServiceLifetime.Scoped)]` on the handler.
- [x] A new test covers a Scoped handler implementing two message interfaces resolving to one instance per scope.
- [x] A new test covers the startup throw when two assemblies contribute handlers for one message type.
- [x] All generator snapshots are regenerated and reviewed rather than accepted wholesale.
- [x] The full suite passes, including the Docker-backed integration tests.
