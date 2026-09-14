# 05: Delete `AddHandler`; exception policies attach through `ForHandler`

**What to build:** The configuration API stops registering handlers and keeps only what discovery
cannot express.

`AddHandler<TH, TM>` does three jobs today: it builds a `ServiceDescriptor`, it records the
(handler, message type) pair, and it returns a `PolicyBuilder`. The first is now the generator's, and
the second exists only to serve the third. What remains is a verb that configures a handler someone
else registered, which is what it should be called.

Exception policies stay in configuration deliberately. They are attached per (handler, message type)
pair rather than per class — `FailedMultipleMessagesHandler` carries a policy on one of its two
message types and none on the other. They differ between compositions of the same handler —
`DiscardFailedMessageHandler` appears under five different policy configurations across the suite.
And `ExceptionPolicy<TEntity>` is an open abstraction whose implementations are resolved from the
container, as `test/Underground.OutboxTest/TestPolicies` demonstrates. No attribute encodes any of
that.

**Blocked by:** 04 (handlers must already be registered by discovery before the manual path is removed).

**Status:** ready-for-agent

- [ ] `AddHandler<TH, TM>` is deleted from both `OutboxServiceConfiguration` and `InboxServiceConfiguration`.
- [ ] `ForHandler<TH, TM>()` replaces it on both, taking no lifetime argument and returning `PolicyBuilder<TEntity>`.
- [ ] `HandlerRegistration<TEntity>` no longer carries a `ServiceDescriptor`, and the `TryAddEnumerable` over registrations is deleted from `SetupServices.AddGenericServices`.
- [ ] `ProcessExceptionFromHandler` still matches policies on `(HandlerType, MessageType)`, and the precedence rules are unchanged: a handler policy beats any global policy, and within a level the nearest matching exception type wins.
- [ ] `cfg.Policies` and the global policy store are untouched.
- [ ] The `[DynamicallyAccessedMembers]` annotations that existed to keep the trimmer honest about `AddHandler`'s `TH` are removed with it.
- [ ] Calling `ForHandler` for a handler discovery did not find throws when the registry is validated at host start, consistent with the duplicate-handler throw, and names the handler and message type.
