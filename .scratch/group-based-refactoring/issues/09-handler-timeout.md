# 09: Handler timeout

**What to build:** A handler that never returns no longer occupies a worker indefinitely or holds a
transaction open. Every handler gets a bounded amount of time, and its cancellation token fires when
that time is up.

**Blocked by:** 08 (shared per-message chain).

**Status:** done

- [x] A configurable maximum handler duration applies to both inbox and outbox handlers.
- [x] The cancellation token passed to a handler is cancelled once that duration elapses.
- [x] A handler cancelled this way is treated as a failed attempt and backs off like any other failure.
- [x] The timeout is a stage in the shared chain rather than duplicated per side.
- [x] A test proves a handler that never returns is cancelled, its message backs off, and the worker goes on to other work.

## Comments

`ServiceConfiguration<TEntity>.HandlerTimeout`, defaulting to the spec's 45 seconds, plus one new
stage, `TimeoutStage<TEntity>`. Both sides get it from `AddGenericServices`, which is the whole of
what 08 bought here: one registration and one line in `MessageChainFactory.Create`, and neither the
inbox nor the outbox was edited.

### Where it sits, and why that is the whole design

Innermost — inside `SavepointStage`, inside `RecordFailureStage`, immediately around the dispatch.
Two constraints pin it there and there is no other position that satisfies both:

- **Inside the savepoint.** A timed-out Handler's writes must be rolled back. `SavepointStage`
  deliberately does *not* roll back on an `OperationCanceledException`, because that means shutdown
  and the transaction is about to be discarded whole. Put the timeout outside, and the savepoint
  never learns anything went wrong.
- **Inside `RecordFailureStage`.** The rollback, `ScheduleRetry`, and the commit all run *after* the
  Handler was cancelled. They need a live token. If the narrowed token reached them, the attempt
  bookkeeping would be cancelled along with the Handler it exists to record.

### Cancellation is translated, not propagated

The stage catches the `OperationCanceledException` and rethrows `HandlerTimeoutException` (a
`TimeoutException`). Both stages outside it step aside for a cancellation, which is right for a
shutdown and wrong for a timeout; a different exception type is what tells them apart. It carries
the message id and the configured duration and it is public, because it surfaces in the error log
that `RecordFailureStage` writes.

The `when` filter is `timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested`
— a shutdown that arrives during a Handler still travels as a cancellation and still stops the
worker. If both fire, shutdown wins, which is the safe way round: the transaction is discarded and
the message is simply left unhandled.

### What is deliberately not done

**No policy consultation.** A timeout goes through `RecordFailureStage`'s general branch rather than
its `MessageHandlerException` one, so exception policies do not see it. They match on
`(HandlerType, MessageType, InnerException)`, and the stage knows none of the first two — the
dispatcher resolves the Handler, not the chain. Routing timeouts to policies would mean threading
the Handler's identity back out of the dispatch for a case where "discard on timeout" is a dubious
thing to want anyway.

The asymmetry this leaves is worth naming: a consumer who writes `OnException<TimeoutException>()`
gets it for a `TimeoutException` their own Handler threw, and does not get it for one this stage
raised. "Backs off like any other failure" is true of the retry and the rollback, which is what the
criterion asks; it is not true of the policy path.

**No opt-out.** `HandlerTimeout` must be greater than zero; there is no `Timeout.InfiniteTimeSpan`
escape. An unbounded Handler is the thing this ticket exists to prevent, and a knob that restores it
would be the first thing reached for when a Handler is slow.

**A Handler that ignores its token is still unbounded.** Nothing here can preempt it, so the ticket's
headline is delivered only for Handlers that observe cancellation — which is all cooperative
cancellation can offer. What the stage guarantees is that the token fires; honouring it remains the
Handler's job.

What happens to a Handler that swallows the cancellation and returns normally anyway depends on
whether it wrote anything, because `DispatchMessage` saves on the narrowed token too. Having written
nothing, it is recorded as handled — it finished, late. Having written something, the save is
cancelled, becomes a `HandlerTimeoutException` in its turn, and the message is rolled back and
retried. The split is not elegant, but both halves are defensible and neither can lose a write.

### Tests

`HandlerTimeoutTests` drives one worker on the calling thread via `ProcessUntilIdleAsync`, so
reaching the second Group is only possible if the first Handler was given up on rather than waited
out. It asserts the hung message is unprocessed with `RetryCount` 1 and pushed ~10 minutes out, and
that the other Group's message was handled.

`BlockingMessageHandler` gained a `Cancelled` signal, set in a `catch` and rethrown. Without it the
test passed for the wrong reason: the handler's own ten-second escape hatch fails the message too,
so every database assertion held whether or not the timeout existed. `Cancelled` distinguishes them,
because the escape hatch raises a `TimeoutException` rather than a cancellation. Verified by
removing the stage from the factory and watching that one assertion — and only that one — fail.

A second test pins the other direction, against cancelling too eagerly: a Handler that really sits
inside its budget — blocked until the test releases it — is processed with `RetryCount` 0 and
`WasCancelled` false.

### What is not tested, and why

Both tests drive the outbox. The inbox gets the stage from the same `AddGenericServices` call and the
same `MessageChainFactory` line, so criterion 1 holds by construction rather than by assertion — but
there is no inbox test here because there is no inbox test anywhere in the repo: nothing references
`InboxMessage` outside `src/`. Every shared behaviour landed by 03 through 08 rests on the same
argument. Building that harness is worth its own ticket and is not smuggled into this one.

Full suite: 60 tests, all passing.
