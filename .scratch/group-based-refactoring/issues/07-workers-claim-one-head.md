# 07: Workers claim one Head at a time

**What to build:** Workers stop being fed work by a discovery stage and start serving themselves.
Each worker claims one Head, handles it, and repeats until nothing is available. The database
distributes Groups across workers through skip-locked semantics, with no coordination and no
lock-failure signalling. Batching goes at the same time: throughput comes from Groups, not from how
many messages fit into one fetch.

**Blocked by:** 06 (two-stage Head discovery).

**Status:** done

- [x] Each worker independently claims one Head, handles it, and repeats until a claim returns nothing, then waits for a trigger or the poll delay.
- [x] The separate work-discovery stage and the channel that fed Group names to workers are removed.
- [x] Batch size is removed from configuration. One message is handled per claim, on both sides. **Done in 06**: skip-locked semantics over a multi-row result are unsound, so batching could not survive that ticket - see its Comments.
- [x] The number of workers is governed by the concurrent-Groups setting, and it is documented that a value of one means strictly serial handling across all Groups.
- [x] The push trigger continues to wake workers immediately after a commit.
- [x] The savepoint is retained on the inbox, isolating a failed handler's writes from the attempt bookkeeping so that both still commit together.
- [x] A test proves distinct Groups are handled concurrently by different workers.
- [x] A test proves a slow handler in one Group does not delay any other Group.

## Comments

The claim query grew back into the spec's shape. 06 had collapsed `DISTINCT ON (group_key)` to a
plain `LIMIT 1` because a worker was still handed one Group at a time; with the discovery stage gone
the CTE has to collect one Head per Group and the outer query take the oldest of them that is both
visible and unlocked. `ClaimHead.ExecuteAsync` lost its `groupKey` parameter, and with it the only
parameter the statement had. `Processor.ProcessHeadAsync` lost it too and reads the Group off the
message it claimed, for logging.

That single statement is now the whole of the work distribution. `FetchGroups` is deleted, the
Groups channel is deleted, and `ProcessNextAsync` is a claim rather than a two-stage take-then-claim.

### The return value had to change meaning

`ProcessNextAsync` now reports whether a message was **claimed**, not whether it was handled
successfully. The spec's wording is "whether a message was handled", but taking that literally makes
a worker stop and sleep after a failure, and with `MaxConcurrentGroups = 1` a single poison message
would then insert the poll delay between every other message in the system - stalling Groups that
have nothing to do with it, against user story 26.

Reporting the claim cannot spin, and the reason is worth stating because it is the whole safety
argument: a message that failed has been pushed out of sight by the backoff, so the next claim looks
straight past its Group at some other Group's Head. The loop terminates for the same reason a
successful one does.

### Waking the pool rather than one worker

The trigger channel is retained but read differently. It used to be drained in the take stage, so a
commit released exactly one worker. `WaitForWorkAsync` now awaits `WaitToReadAsync`, which releases
*every* idle worker, and then drains the token so that the next wait blocks again. Whichever worker
wins that race is immaterial - they have all been released by then. Without this, a commit arriving
at an idle pool would be served by one worker handling every Group serially until the next poll.

The initial `ScheduleProcessingRun()` in `RunAsync` is gone: workers now begin with a claim rather
than with a wait, so a backlog present at startup is picked up without being told about.

A failed claim reports "no work" rather than "try again", so a database that is refusing connections
is waited on for the poll delay instead of being hammered in a tight loop. The worker-level
try/catch went with it - it was duplicating the one inside `ProcessNextAsync`.

### The tests

`SlowHandlerInOneGroupDoesNotDelayAnotherGroup` runs two workers, holds one of them inside a handler
on a signal, and requires the other to finish a whole second Group unaided. It asserts the slow
message is still unhandled at that point, so it cannot pass vacuously by the slow Group simply
finishing first. `BlockingMessageHandler` is new and blocks on a `TaskCompletionSource` rather than a
duration, so nothing sleeps.

`DistributeGroupsAcrossWorkersEqually` already proved concurrent handling of distinct Groups - its
barrier only completes when four Groups are genuinely in flight at once - and it still does under
self-serving workers, because the four workers now all claim at startup instead of waiting to be
handed a Group.

`ProcessUntilIdleAsync` no longer schedules a run first; it just drives `ProcessNextAsync` until a
claim comes back empty.

### Two things a review raised that are the spec's to answer, not this ticket's

**The claim query now scans every unprocessed row, once per message handled.** PostgreSQL has no
loose index scan, so `DISTINCT ON (group_key)` walks the whole partial index rather than skipping
from one Group to the next. The spec (Schema) claims the partial index makes "Head discovery cost
proportional to the number of Groups rather than the number of rows"; that is true of the rows kept
for the retention period, which the filter excludes, but not of the unprocessed ones. The old design
paid the same scan in `FetchGroups`, but once per discovery cycle amortised across every Group it
found, where this pays it per claim. Draining a large backlog is therefore quadratic in index reads.

This is the query shape the spec prescribes and 06 was told to follow closely, so it is not changed
here. The comment on the index in `MessageConfiguration` was overstating the same thing and has been
corrected to say what the filter actually buys. If it needs fixing, the fix is a `LIMIT` on the CTE
plus accepting that workers contend on a short candidate list, or a recursive skip-scan - both are
changes to the spec's query, not to this ticket.

**A slow handler does delay other Groups' *newly committed* messages.** `ClaimHead` takes `FOR
UPDATE`, which assigns the transaction a real xid, and the inbox holds that transaction open across
the handler. `pg_snapshot_xmin` is therefore pinned for as long as the slowest handler runs, and
nothing committed during that window is Settled - in any Group. So this ticket's criterion holds for
messages that were already committed, which is what `SlowHandlerInOneGroupDoesNotDelayAnotherGroup`
arranges, and not for ones arriving during the slow call.

This is not new and not introduced here: it is the Settled rule's documented consequence, recorded in
ADR 0002 ("a long-running writer holds the snapshot minimum back and stalls all message delivery")
and in user story 34. 10 removes it for the outbox, which is the entire point of dispatching with no
transaction open; the inbox keeps it by design, in exchange for exactly-once.

### README

Only the sentences this ticket falsified were touched - the discovery stage, "processed in batches",
the two-stage description of skip-locked contention, and the batch language in error handling - plus
the `MaxConcurrentGroups` row, which this ticket's criteria require. The rest of the README is 11's.
