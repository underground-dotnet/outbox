# 06: Two-stage Head discovery

**What to build:** A Group offers only its Head, and it offers nothing at all when that Head is not
yet visible. This is the change that makes ordering survive backoff. The naive query — filter by
visibility, then take the oldest — hands out the second message of a Group whose first message is
waiting to be retried, which is exactly the reordering this work exists to prevent. It compiles,
and it passes any test that does not involve a failed message.

The Head must therefore be determined **without regard to visibility**, and visibility tested
against that one message afterwards. The feature spec alongside this ticket carries the query shape
and the three constraints that make it correct; follow it closely. This is the single most likely
place in the whole feature for a defect to hide.

**Blocked by:** 04 (retry backoff via a visibility instant).

**Status:** done

- [x] A Group's Head is its oldest settled unhandled message, determined without applying any visibility filter.
- [x] Visibility is tested against the Head alone. A Group whose Head is not yet visible contributes nothing.
- [x] Row locking uses skip-locked semantics, so one busy Group no longer aborts the query for every other Group.
- [x] The lock-unavailable error path previously used as flow control is removed.
- [x] A test proves that while a Group's Head is in backoff, no later message in that Group is handled, however long it has waited.
- [x] A test proves other Groups continue to be handled while one Group's Head is unavailable.
- [x] A test proves that once the Head becomes visible again it is handled before anything behind it.
- [x] A test proves the same for a Head that is invisible because it was *scheduled* rather than because it failed. Carried over from 05, whose implementation landed without it: the behaviour is this ticket's to deliver.

## Comments

The query follows the spec's shape with one simplification and one addition. The `DISTINCT ON
(group_key)` collapses to a plain `LIMIT 1`, because a worker is still handed one Group at a time
by the discovery stage that 07 removes — the CTE only ever needs that Group's Head. And the outer
`WHERE` repeats `processed_at IS NULL` alongside the visibility test. The CTE reads its snapshot
without a lock, so if a concurrent worker commits `processed_at` between the snapshot and the lock
being taken, `FOR UPDATE` re-evaluates only the outer `WHERE` against the new row version; without
the repeated check that race hands out an already-handled message.

`FetchMessages` was renamed to `ClaimHead` (and its two subclasses to `ClaimInboxHead` /
`ClaimOutboxHead`) to match: it no longer fetches messages, it claims one Head.

### Batching had to go here rather than in 07

07 owns removing `BatchSize`, but it is not separable from this ticket. Two independent reasons:

- `SKIP LOCKED` over a multi-row result is unsound. If the second of three rows is locked by
  another worker, the statement returns the first and third — the reordering this ticket exists to
  prevent, reintroduced by the locking clause the same ticket mandates.
- Head-gating a batch is either wrong or expensive. Taking the Head plus its followers, gated only
  on the Head's visibility, hands out a *scheduled* second message early. Taking the contiguous
  visible prefix instead needs a window function over the ordered rows, and 07 deletes it again.

So `ProcessMessagesAsync(groupKey, batchSize, …)` became `ProcessHeadAsync(groupKey, …)`, handling
one message per claim in one transaction, and `BatchSize` left the configuration and the README.
What remains for 07 is the worker topology: self-serving workers, the discovery stage, and the
Groups channel.

### Which tests actually distinguish the two queries

All four do, but only after deliberate work, and it is worth recording what that work was.
`MessagesBehindAHeadInBackoffAreNotHandledEvenThoughTheyAreVisible` and
`MessagesBehindAScheduledHeadAreNotHandledUntilThatHeadHasBeen` were run against the single-stage
`WHERE visible_at <= clock_timestamp()` form and fail on it: the naive query hands out the message
behind the invisible Head. The other two — other Groups continuing, and the Head going first once it
recovers — passed against both forms as first written, because one run stops at the failed Head
either way; a second idle run in each is what makes them discriminate.

`RecoveringMessageHandler` is new and exists so that the failing message and the message behind it
share one handler, and with it one ordered list. Asserting order across `FailedMessageHandler` and
`ExampleMessageHandler` would mean comparing two collections that record no relative order.

### Two things left as they are

`ProcessedAt` is still written as `DateTime.UtcNow` through `ExecuteUpdateAsync`, against the spec's
rule that every instant is computed by the database. It is not a new deviation — the line was only
moved here — and 10 rewrites this exact statement as the guarded `SET processed_at =
clock_timestamp() WHERE id = @id AND visible_at = @granted`, so writing a raw-SQL completion now
would be thrown away. Nothing depends on the value beyond the retention sweep.

Nothing tests skip-locked semantics directly. Proving it needs two workers contending for one Group,
which is the concurrency fixture 07 builds; the clause it replaced had no test either. The other
three criteria are covered by `HeadDiscoveryTests`.

### An unrelated flake fixed on the way

`ProcessMessagesOnSaveChangesInterceptorTests` starts a hosted service that appends to
`ExampleMessageHandler.CalledWith`, and it was in no xunit collection, so it ran in parallel with
the Domain tests that assert on that same static list. It surfaced as
`SettledOrderingTests.MessageIsWithheldUntilNoOlderTransactionCouldStillInsertAheadOfIt` failing
with "Collection was not empty" roughly one full-suite run in four. Pre-existing rather than caused
here — this ticket only made it visible by lengthening the serialized collection — and fixed by
putting the class into the collection that serializes the others.
