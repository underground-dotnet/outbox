# 08: Shared per-message chain

**What to build:** The work done to one message — tracing, exception policy, backoff computation,
savepoint, dispatch — is expressed once as an ordered chain used by both the inbox and the outbox,
so that a change to any of it cannot be applied to one side and forgotten on the other. Nothing
behaves differently. This is a prefactor for the outbox Lease work, and it lands here rather than
earlier because the shape of one step is not settled until 07.

**Blocked by:** 07 (workers claim one Head at a time).

**Status:** done

- [x] Per-message concerns are expressed as a single ordered chain rather than inline in the processing loop.
- [x] Both sides use the same chain. The savepoint stage is simply absent from the side where no transaction is open. *(Vacuous today: no side dispatches without a transaction until 10 — see Comments.)*
- [x] The chain is assembled by an internal factory and is not publicly configurable, because the order between stages is a correctness property rather than a preference.
- [x] Transaction boundaries, claiming a Head, and writing the outcome remain outside the chain.
- [x] The existing test suite passes with no behavioural change.

## Comments

`Processor<TEntity>.CallMessageHandlerAsync` is gone. What it did is now four types under
`Domain/Chain/`, composed outermost-first by `MessageChainFactory.Create`:

| Stage | Was |
|---|---|
| `LogMessageStage` | the `LogProcessingMessage` call in `ProcessHeadAsync` |
| `RecordFailureStage` | the two catch blocks, the exception policies, and `ScheduleRetry` |
| `SavepointStage` | create / release / roll back to `processing_message_{id}` |
| `DispatchMessage` | `IMessageDispatcher.ExecuteAsync` plus the trailing `SaveChanges` |

`Processor` keeps exactly what 08 says it should: begin the transaction, claim the Head, run the
chain, write `ProcessedAt` if it reported success, commit.

### Why the order is the way it is

`RecordFailureStage` has to sit *outside* `SavepointStage`, because the retry count and the new
`VisibleAt` it writes must survive the rollback that throws away the failed Handler's writes. That
is the whole reason the savepoint exists, and it is the one ordering constraint in the chain that
would produce a silent data bug rather than a compile error if it were got wrong. It is stated on
the factory rather than left to be re-derived.

### Vocabulary

`Stage` and `Chain` were new nouns with no glossary entry, so `CONTEXT.md` gained both.

### Nothing configurable

The factory is an internal static and takes no options. A public builder would let a consumer put
the savepoint outside the retry, or drop the dispatch, and each of those is a correctness bug rather
than a preference.

### What "both sides use the same chain" is worth

Less than it sounds, on its own: `Processor<TEntity>` was already generic and already shared by both
sides before this ticket, so nothing was unified that was not already unified. What changes is that
the shared thing is now a list of named stages rather than one method, which is what lets 09 add a
stage and 10 remove one without either side being edited separately.

### The savepoint criterion has no side to be absent from yet

"Absent from the side where no transaction is open" describes the outbox *after* 10. Today both
sides hold one transaction across the dispatch, so both chains are identical and both include the
savepoint. What this ticket buys is that removing it in 10 is deleting one line of the factory
rather than unpicking a method. The mechanism is there; the asymmetry is 10's to introduce.

### Two deliberate non-changes

`HandleMessageStep` takes a `CancellationToken` rather than closing over one, so that 09 can insert
a timeout stage that narrows the token for everything inside it without touching anything outside.

`IServiceScope` is threaded through the chain as a parameter instead of being injected. It is not
a registered service, and the dispatcher needs it to resolve the Handler.

### Behaviour differences, such as they are

Two things move on the failure path without changing what a consumer can observe:

- The order of three steps changed. It was log → `ChangeTracker.Clear()` → rollback; it is now
  rollback → log → `Clear()`, because `RecordFailureStage` only sees the exception once
  `SavepointStage` has rethrown it. What matters is that both the rollback and the clear still
  precede the exception policies, so a policy still gets a clean change tracker and a rolled-back
  savepoint. The relative order of a log line and a `ROLLBACK TO SAVEPOINT` is not observable.
- `ChangeTracker.Clear()` moved from the savepoint's territory into `RecordFailureStage`. It is
  in-memory only and independent of the rollback, and it belongs with the policies it exists to
  serve — which is also what makes it correct for an outbox chain that will have no savepoint stage.

Logger categories changed from `Processor<T>` to the stage types. The `EventId`s did not:
`LogMessageStage` keeps 1, and `RecordFailureStage` keeps 2 and 3 rather than renumbering from 1
inside its own category.

`ProcessExceptionFromHandler` is still resolved from the scope passed to `ProcessHeadAsync` rather
than constructor-injected. Constructor injection would usually be the same instance, but only
because `ConcurrentProcessor` happens to resolve the `Processor` from the scope it then passes;
`ProcessorErrorTests.ExceptionPolicyOnlyAppliesToConfiguredMessageTypeForMultiMessageHandler` passes
a different one, and an exception handler should see the services the Handler that raised it saw.

`SavepointStage` keeps the original's treatment of `OperationCanceledException`: it is not rolled
back to, because the transaction is about to be disposed whole and another statement on a cancelled
token would only fail again.

### One difference that is real, on a path nothing exercises

`CreateSavepointAsync` used to sit outside every `try`, so a failure to *create* the savepoint
propagated straight to `ConcurrentProcessor`, which logged it and reported no work. Nesting puts it
inside `RecordFailureStage`'s `try`, so it is now caught and `ScheduleRetry` is attempted on what is
almost certainly a broken connection or an aborted transaction. That attempt then throws in turn and
propagates to the same place, so the worker ends up in the same state by the same route, one extra
error log and one doomed round-trip later. `RollbackToSavepointAsync` failures moved the same way.

Restoring the exact previous classification would mean giving the savepoint's own mechanics a
distinct exception type for `RecordFailureStage`'s filter to exclude — machinery for a path where
the database has already failed, and where both behaviours converge. Left as is, deliberately, and
recorded here rather than discovered later.

The full suite (57 tests) passes unchanged.
