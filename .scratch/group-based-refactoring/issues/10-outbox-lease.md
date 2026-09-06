# 10: Outbox Lease and three-transaction delivery

**What to build:** Outbox handlers stop holding a database transaction across a slow external call.
A worker takes a time-bounded Lease and commits it, dispatches with nothing open, then writes the
outcome in a separate transaction. A worker that dies no longer blocks its Group: the Lease simply
expires.

The cost is at-least-once delivery, which is why every write is guarded on the Lease that was
granted. Without the guard, a worker finishing after its Lease had expired would overwrite a newer
worker's claim and fan the message out to a third worker.

This is where the outbox delivery guarantee changes, and it is deliberately the last behavioural
change so that it can be reverted on its own.

**Blocked by:** 09 (handler timeout).

**Status:** done

- [x] Claiming an outbox message commits a time-bounded Lease in its own transaction, before the handler runs.
- [x] The outbox handler runs with no database transaction open.
- [x] Success and failure are written in a separate transaction, each guarded on the Lease instant granted at claim time.
- [x] A guarded write that affects no rows is reported as a lost Lease at warning level. It does not throw, and it does not mark the message handled.
- [x] The Lease duration is derived from the handler timeout plus a margin for the completion write, and is not separately configurable, so a Lease shorter than the timeout is impossible to configure.
- [x] The handler's cancellation fires before its Lease expires, so a message can never be taken from a live worker.
- [x] Inbox handling continues to run entirely within one transaction and remains exactly-once.
- [x] A test expires a Lease, re-claims the message as a second worker, then lets the first worker finish, and proves the first neither overwrites the second's claim nor marks the message handled.
- [x] A test proves a message whose worker died becomes available again once its Lease expires.

## Comments

### The Lease is the claim, not a second write

`ClaimOutboxHead` no longer selects the Head and locks it; it *updates* it, in one statement:

```sql
WITH heads AS (...), claimed AS (... FOR UPDATE OF m SKIP LOCKED)
UPDATE outbox m SET visible_at = clock_timestamp() + @lease
FROM claimed c WHERE m.id = c.id
RETURNING m.id, ..., m.visible_at, ...
```

Granting the Lease *is* the claim, so there is no window in which a worker holds a message without
one. `RETURNING` hands back the granted instant, which becomes the guard on every later write. The
interval is a parameter and the instant is computed by PostgreSQL, so an instance with a skewed
clock cannot expire its own Lease early.

Head discovery moved into `ClaimHead.LockedHeadCte`, shared by both sides, with the projection in
`ClaimHead.Projection` so the outbox's `RETURNING` list cannot drift from the inbox's `SELECT`. The
inbox statement is otherwise unchanged: it reads the row back out of the CTE and keeps the row lock
for its transaction. Nothing about inbox behaviour moved.

### Three classes where there was one

`Processor<TEntity>` became the interface `IProcessor<TEntity>` with `InboxProcessor` and
`OutboxProcessor` behind it. The transaction boundary is precisely what differs between the sides
and a flag would have meant every line branching on it. `ConcurrentProcessor` resolves the interface
and is unchanged; `TEntity` appears in no signature on it and is there to select the implementation,
which needed an S2326 suppression.

`MessageChainFactory` grew `CreateInbox` and `CreateOutbox`. They differ by exactly one stage:
`SavepointStage` is absent from the outbox, which was already what its own doc comment predicted.
The two `AddScoped` calls for the chain and the processor moved out of `AddGenericServices` into the
per-side setup methods.

### The guard, and where it had to go

`GuardedWrite<TEntity>` is the new base for the two writes that end a claimed message's turn:
`MarkHandled<TEntity>` (new) and `ScheduleRetry<TEntity>` (existing). It owns the statement cache,
binds `@id` and `@lease`, appends the guard through `Guard(table)`, and turns zero rows affected into
`false` plus one warning — one message, one event id, so the rate is countable from either path. A
subclass supplies only its statement, so a third outcome cannot be added with the guard left off.

The statement cache is keyed by concrete type as well as by model, because both subclasses close over
the same `TEntity` and so share the static field; keyed by model alone they would serve each other's
SQL.

The guard is written identically on both sides. On the inbox it is trivially satisfied: the row is
locked for the whole transaction, so nothing can have moved its visibility instant. A predicate that
is always true is cheaper than a second statement per side, and it means neither side has a write
path the other does not.

"Separate transaction" on the outbox means each of these statements autocommits on its own — there is
no ambient transaction to join, which is the point.

A `Lease` value type wrapping the granted instant was considered and dropped. The guard reads
`VisibleAt` precisely because one column serves scheduled delivery, backoff and Lease expiry alike;
a distinct type would have to be unwrapped back to that column at every use and would push a
lease-shaped concept onto the inbox, which has none.

**`RecordFailureStage` now writes the retry before it consults the exception policies**, which is
the one ordering change. The retry is the guarded write — it is what establishes that this worker
still holds the message — and an exception policy is consumer code that writes by id and cannot be
guarded. A lost Lease has to be discovered before a `Discard()` policy is allowed to delete a
message some other worker now owns. The final row state is unchanged for every existing policy test.

### Lease duration

`ServiceConfiguration.LeaseDuration` is `HandlerTimeout + 15s`, internal, with no setter. The margin
is a constant because the only thing a second knob could express is a Lease shorter than the
timeout, which guarantees double delivery on every slow message. `TimeoutStage` cancels the Handler
at `HandlerTimeout`, so the completion write always has the margin in hand.

This is a guarantee about when the token *fires*, not about when the Handler stops: a Handler that
ignores cancellation can still overrun its Lease, and the guard is what catches that.

### What this costs, and the four tests that had to change

An outbox Handler's own database writes are no longer rolled back when it throws, because there is
no transaction and no savepoint to roll back to. Four tests in `ProcessorErrorTests` encoded the old
guarantee and now pin the new one:

- `OutboxTransactionIsUsedByInjectedDbContext` → `OutboxHandlerRunsWithNoTransactionOpen`, asserting
  the Handler's `CurrentTransaction` is null. It gained a `WasCalled` flag, because null on its own
  would also hold if the Handler never ran.
- `RollbackHandlerDbChangesOnError` and `RollbackHandlerCustomSqlChangesOnError` →
  `Handler…SurviveAnErrorBecauseTheOutboxHoldsNoTransaction`, one per write route.
- `KeepDbChangesFromSuccessfullMessagesOnFailure` keeps its name and now asserts the successful
  message's write is present rather than counting rows, since the failed message's write survives too.

`CustomSqlMessageHandler` had a real bug: it inserted into `"Users"` while the table is mapped
`users`, so the statement always failed and the test asserted an empty table for the wrong reason.
Fixed, which is what turned that test from vacuous into meaningful.

A consumer who was relying on outbox handler writes being rolled back has to move that work to the
inbox or make it idempotent. That is ADR 0001's stated consequence, not a new one, but this is the
commit where it starts to bite.

### Tests

`OutboxLeaseTests`, three of them, all driving `ProcessNextAsync` against real PostgreSQL with no
sleeping — the Lease is expired by moving `visible_at`, which is indistinguishable from waiting
because all timing is server-side.

- `MessageWhoseWorkerDiedIsOfferedAgainOnceItsLeaseExpires` — claims and abandons, proves the Group
  offers nothing while the Lease runs, then proves expiry alone is enough to get the message handled.
- `WorkerThatLostItsLeaseNeitherOverwritesTheNewClaimNorMarksTheMessageHandled` — the completion
  guard. Verified by removing `AND visible_at = @lease` from `MarkHandled` and watching this test,
  and only this test, fail.
- `HandlerThatTimesOutStillHoldsItsLeaseWhenItRecordsTheAttempt` — the other direction: a Handler
  that used its whole budget still writes its attempt, so nothing can take a message from a live
  worker.

The warning is asserted through a new `RecordingLoggerProvider`, because a lost Lease is deliberately
reported rather than thrown and the row it leaves behind is the *other* worker's, so there is nothing
else to observe.

### What is deliberately not done

No inbox test, for the same reason as 09: nothing in the test project references `InboxMessage`.
Criterion 7 holds by `InboxProcessor` being the unedited body of the old `Processor` plus the
guarded completion write.

No metric for the lost Lease. The spec puts OpenTelemetry instruments out of scope; the warning has
a stable event id so counting it later is mechanical.

Full suite: 63 tests, all passing.

## Review

Reviewed on two axes. The Standards pass produced four changes, all applied: XML docs on the new
public test members (`.editorconfig` sets `CS1591` to none, so the build does not catch these);
`GuardedWrite` extracted to remove the duplicated guarded-write shape; the misnamed `Lease` class —
which held a log message and no lease — deleted into it; and the unused `alias` parameter dropped
from `ClaimHead.Projection`.

The Spec pass was verified by hand rather than by an agent, which died on a rate limit before
starting. All nine criteria hold. Three changes go beyond what the ticket asked for and are named
here rather than buried: the `RecordFailureStage` reordering (argued above), the `Processor` →
`IProcessor` rename rippling through the test project, and the `CustomSqlMessageHandler` SQL fix.

### For ticket 11

`docs/adr/0003-no-batching.md` argues from "a 45-second lease" and works an example off it. The
Lease is now 60 seconds — `HandlerTimeout` plus the margin — so the number in that ADR is stale. The
argument is unaffected; only the arithmetic needs a pass. Not changed here, because editing a
decision record to match an implementation belongs with the rest of the documentation correction.
