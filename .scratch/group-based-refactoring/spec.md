# Group-based processing with visibility timestamps

Status: ready-for-agent

## Problem Statement

Teams running `Underground.Outbox` on more than one application instance hit four problems that the
current design cannot express.

A handler that fails is retried immediately on the very next cycle, forever, with no delay. A
message that can never succeed spins in a hot loop and, because messages within a Group are handled
in order, starves everything behind it. There is no way to say "try this again in thirty seconds."

There is no way to say "deliver this at nine tomorrow morning" either. Scheduling a message means
building a scheduler outside the library.

Outbox handlers usually talk to something slow and external — Kafka, an HTTP API. Today the
database transaction that fetched the message stays open for the entire handler call, so a slow
partner API holds a Postgres transaction open across the network. Inbox handlers have the opposite
need: they apply business logic to the same database and genuinely want that single transaction.

Finally, ordering is promised but not delivered. Messages are ordered by an identity column, and
identity values are assigned when a row is inserted rather than when its transaction commits, so
two producers writing to the same Group concurrently can have their messages handled in the wrong
order with nothing reporting an error.

## Solution

Every message gains a `VisibleAt` timestamp — the instant from which it may be handled. One column
serves three purposes: scheduled delivery, retry backoff, and (on the outbox) lease expiry. This is
the `vt` concept from pgmq, reimplemented as ordinary SQL queries so that no Postgres functions,
extension, or `pg_partman` installation is required.

Every message also gains a `TransactionId`, and a message becomes eligible for handling only once
it is **Settled** — once no still-running transaction could yet insert an earlier message into its
Group. Combined with ordering by `(TransactionId, Id)`, this makes ordering within a Group total
rather than approximate.

A Group offers only its **Head** — its oldest Settled unhandled message. Workers claim Heads
independently, one message at a time, and the database distributes Groups across them. Different
Groups proceed concurrently; a Group never has two messages in flight.

The two sides then diverge where their needs genuinely differ. An inbox worker holds one
transaction across the handler, so an inbox message is applied exactly once. An outbox worker takes
a short, time-bounded **Lease**, commits it, dispatches with no transaction open, and completes in a
second short transaction — so a slow external call never holds a transaction, at the price of
at-least-once delivery.

## User Stories

1. As an application developer, I want a failing message to be retried with an increasing delay, so that a temporarily unavailable partner system is not hammered with requests.
2. As an application developer, I want retry delays to be capped, so that a message that recovers after a long outage is picked up again within a predictable time.
3. As an application developer, I want retry delays to be jittered, so that when a shared dependency fails and every Group backs off at once they do not all retry in lockstep.
4. As an application developer, I want to schedule a message for a future instant, so that I can express "send this reminder tomorrow morning" without building a scheduler.
5. As an application developer, I want scheduling to default to "immediately", so that existing code that creates messages keeps working unchanged.
6. As an application developer, I want to know that scheduling a message delays its whole Group, so that I do not accidentally stall an ordered stream by scheduling one message far in the future.
7. As an outbox handler author, I want my handler to run with no database transaction open, so that a slow HTTP or Kafka call does not hold a Postgres transaction across the network.
8. As an outbox handler author, I want to know my handler may be invoked more than once for the same message, so that I write it to be idempotent.
9. As an outbox handler author, I want a bounded amount of time to complete, so that a hung external call cannot occupy a worker indefinitely.
10. As an outbox handler author, I want to be cancelled before my Lease expires rather than after, so that a slow-but-healthy call fails cleanly instead of being silently double-delivered.
11. As an inbox handler author, I want my database writes and the record that the message was handled to commit together, so that the message is applied exactly once.
12. As an inbox handler author, I want my writes rolled back when I throw, while the attempt is still recorded, so that a failure does not leave half-applied state and does not lose the retry count.
13. As an inbox handler author, I want a bounded amount of time to complete, so that a runaway handler cannot hold a transaction open and accumulate locks and table bloat.
14. As an application developer, I want messages in the same Group handled strictly in order, so that per-customer or per-aggregate sequences are never applied out of sequence.
15. As an application developer, I want that ordering to hold even when two of my own transactions write to the same Group concurrently, so that ordering is a guarantee rather than a probability.
16. As an application developer, I want to understand that ordering reflects when a transaction started rather than when a message was appended, so that I am not surprised in the rare cases where those differ.
17. As an application developer, I want different Groups handled concurrently, so that throughput scales with how many Groups my application defines.
18. As an application developer, I want to configure how many Groups are handled at once, so that I can match concurrency to my database and downstream capacity.
19. As an application developer, I want a slow handler in one Group not to delay any other Group, so that one slow customer does not stall every other customer.
20. As an operator, I want to run many application instances against one database, so that I can scale out and survive the loss of an instance.
21. As an operator, I want two instances never to handle the same message at the same time under normal operation, so that duplicate effects stay rare rather than routine.
22. As an operator, I want a message held by an instance that dies to become available again automatically, so that a crashed pod does not require manual intervention.
23. As an operator, I want the delay before that recovery to be configurable, so that I can trade recovery speed against how long a slow handler is allowed to run.
24. As an operator, I want a worker that finishes after its Lease expired to detect that it lost the Lease, so that it cannot overwrite a newer worker's claim and fan the message out further.
25. As an operator, I want losing a Lease to be reported as a warning rather than swallowed or thrown, so that I can see how often it happens without it breaking processing.
26. As an operator, I want a permanently failing message to stall only its own Group, so that the blast radius of one bad message is bounded.
27. As an operator, I want the vocabulary in logs and configuration to say "Group" rather than "partition", so that it is never confused with PostgreSQL table partitioning.
28. As an application developer, I want to discard a message for a known exception type, so that a known-bad message does not stall its Group while a dead-letter mechanism does not yet exist.
29. As an application developer, I want handled inbox messages retained long enough to reject a redelivered event, so that duplicate suppression actually works for as long as I expect.
30. As an application developer, I want the retention setting's role in duplicate suppression to be documented on the inbox, so that I do not shorten it and silently start accepting duplicates.
31. As an application developer, I want handled messages cleaned up automatically after their retention period, so that the tables do not grow without bound.
32. As an operator, I want Head lookup to stay fast as handled messages accumulate, so that processing latency does not degrade over the retention window.
33. As an operator, I want all timing decided by the database rather than by application clocks, so that an instance with a skewed clock cannot expire its own Leases early and cause systematic duplicates.
34. As an operator, I want to understand that a long-running write transaction anywhere in the database delays delivery, so that I can monitor for it rather than be mystified by it.
35. As a library maintainer, I want the inbox and outbox to share all per-message logic, so that a change to retry, logging, or exception handling cannot be applied to one side and forgotten on the other.
36. As a library maintainer, I want the parts that genuinely differ between the two sides to be written out explicitly rather than hidden behind a mode flag, so that the difference is visible at the point where it matters.
37. As a library maintainer, I want no test-only hooks in production types, so that production code is shaped by production needs.
38. As a library maintainer, I want time-dependent behaviour testable without sleeping, so that the suite is fast and not flaky.
39. As a library maintainer, I want the change delivered as a reviewable sequence, so that the ordering fix and the renames can land and be verified before the delivery guarantee changes.

## Implementation Decisions

### Vocabulary

`CONTEXT.md` is authoritative. `PartitionKey` becomes `GroupKey` everywhere, including
`MessageMetadata` and the source-generator output and snapshots. "Partition" is reserved for
PostgreSQL declarative table partitioning and must not appear in this feature's names, logs, or
configuration.

### Schema

Identical on both message tables. New columns are `TransactionId` and `VisibleAt`; `PartitionKey`
is renamed; everything else is unchanged.

| Column | Type | Notes |
|---|---|---|
| `Id` | `bigint` identity, primary key | insertion order; never the sort key on its own |
| `EventId` | `uuid`, unique | duplicate suppression |
| `TransactionId` | `xid8 NOT NULL DEFAULT pg_current_xact_id()` | new |
| `CreatedAt` | `timestamptz NOT NULL` | |
| `Type` | `text NOT NULL` | |
| `GroupKey` | `text NOT NULL` | renamed |
| `Data` | `jsonb` (outbox) / `text` (inbox) | unchanged |
| `RetryCount` | `int NOT NULL DEFAULT 0` | incremented on observed failure only |
| `VisibleAt` | `timestamptz NOT NULL DEFAULT clock_timestamp()` | new |
| `ProcessedAt` | `timestamptz NULL` | retained until the retention sweep |

A partial index on `(GroupKey, TransactionId, Id) WHERE ProcessedAt IS NULL` serves Head discovery.
It must be partial: with a week of retention the table is mostly handled messages, and Head
discovery never looks at them. This makes Head discovery cost proportional to the number of Groups
rather than the number of rows.

Consumers own their EF migrations, so the schema change is theirs to generate and apply. The
library is unpublished, so no backwards compatibility is required.

### Head discovery

Head discovery is deliberately two-stage, and getting this wrong is the single most likely defect in
the whole feature. The Head is the lowest `(TransactionId, Id)` among Settled, unhandled rows
**regardless of visibility**; only then is visibility tested against that one row. Filtering by
`VisibleAt` first would hand out the second message of a Group whose Head is in backoff — precisely
the ordering violation this feature exists to prevent.

```sql
WITH heads AS (
    SELECT DISTINCT ON (group_key) id
    FROM outbox
    WHERE processed_at IS NULL
      AND transaction_id < pg_snapshot_xmin(pg_current_snapshot())
    ORDER BY group_key, transaction_id, id
)
SELECT o.id, ...
FROM heads h
JOIN outbox o ON o.id = h.id
WHERE o.visible_at <= clock_timestamp()
ORDER BY o.transaction_id, o.id
LIMIT 1
FOR UPDATE OF o SKIP LOCKED
```

Three constraints are load-bearing:

- Applying the Settled filter during Head discovery is safe **only because** the sort key is
  `(TransactionId, Id)`. An unsettled row always sorts after every Settled one, so excluding it can
  never promote a later message. With `Id` alone this would be a bug.
- `FOR UPDATE` cannot appear alongside `DISTINCT ON`, so the Head set must be a CTE and the locking
  clause applied to the outer join back to the table.
- `SKIP LOCKED`, not `NOWAIT`. A single query now spans many candidate Groups, and `NOWAIT` would
  abort the whole statement because one Group was busy. The existing `55P03`-as-flow-control
  handling is deleted.

The outbox wraps this CTE in an `UPDATE ... RETURNING` that sets `VisibleAt` to the Lease expiry and
returns it. The inbox uses the `SELECT ... FOR UPDATE` form and holds the lock for the transaction.

### Time

Every instant is computed by PostgreSQL from `clock_timestamp()`; the application supplies only
*intervals*. Absolute timestamps from the application would make Lease expiry depend on each
instance's clock. `clock_timestamp()` rather than `now()` throughout, because `now()` is frozen for
the transaction and the inbox holds one open across its handler.

### Outbox delivery

Claim, dispatch, and completion are three separate transactions, per ADR 0001. The claim returns the
granted `VisibleAt`; every subsequent write to that message is guarded on it:

```sql
UPDATE outbox SET processed_at = clock_timestamp()
WHERE id = @id AND visible_at = @grantedVisibleAt
```

Zero rows affected means the Lease was lost and another worker owns the message. That is a warning
and a metric, not an exception. Without the guard, a worker finishing after its Lease expired would
overwrite a live Lease and fan the message out to a third worker.

### Inbox delivery

One transaction spans claim, handler, and completion. The savepoint is retained despite there being
no batch: it isolates a failed handler's writes from the `RetryCount` and `VisibleAt` bookkeeping so
that both still commit together. Without it the failure path would need a second transaction and a
lock gap.

### Retry and scheduling

`VisibleAt` is set to `clock_timestamp() + interval` where the interval is
`min(BackoffBase * 2^RetryCount, MaxBackoff)` with jitter applied. A permanently failing message
retries forever and stalls its Group; this is deliberate and recorded in ADR 0004. Message
constructors gain an optional scheduling parameter defaulting to "immediately".

### Worker model

Each of `MaxConcurrentGroups` workers loops independently: claim one Head, handle it, complete it,
repeat until the claim returns nothing, then wait for a trigger or the poll delay. `SKIP LOCKED`
distributes Groups across workers with no coordination. The distinct partition-discovery stage, the
partitions channel, and the `55P03` catch block are all removed. The trigger channel that backs
`IOutbox.ProcessMessages()` is retained as the wake-up signal.

The worker loop's single step is a `ProcessNextAsync(CancellationToken)` returning whether a message
was handled. This is the production decomposition and also the primary test seam.

### Per-message logic

Per-message concerns — tracing, exception policy, backoff computation, savepoint, dispatch — live in
one middleware chain shared by both sides, assembled by an internal factory rather than a public
builder, because the ordering between stages is a correctness property. The savepoint stage is
simply absent from the outbox chain.

Transaction boundaries, how a Head is claimed, and the completion statement stay in two explicit
outer loops. These are not expressed as middleware: an outbox worker has no transaction open during
dispatch and nothing to roll back, so a shared context object would have to carry nullable
transaction and Lease fields and every stage would branch on which was present.

### No batching

One message per claim on both sides, per ADR 0003. `BatchSize` is removed.

### Configuration

| Setting | Default | Notes |
|---|---|---|
| `MaxConcurrentGroups` | 4 | renamed from `ParallelProcessingOfPartitions`; 1 means strictly serial across all Groups |
| `HandlerTimeout` | 45s | maximum handler runtime; the outbox Lease is derived from it, never configured separately |
| `BackoffBase` | 1s | |
| `MaxBackoff` | 10min | |
| `BackoffJitter` | 0.2 | plus or minus 20% |
| `ProcessingDelayMilliseconds` | 4000 | unchanged |
| `ProcessedMessageRetention` | 7d | on the inbox this is the duplicate-suppression window and must be documented as such |
| `CleanupDelaySeconds` | 3600 | unchanged |

`HandlerTimeout` is the only knob; the Lease is `HandlerTimeout` plus a grace margin for the
completion write. Exposing both would allow someone to set a Lease shorter than the timeout, which
guarantees double delivery on every slow message. The handler's cancellation token fires before the
Lease expires, so a message can never be stolen from a live worker.

### Delivery sequence

1. Rename `PartitionKey` to `GroupKey` throughout, including `MessageMetadata` and generator snapshots. Mechanical, no behaviour change.
2. Add `TransactionId`, the Settled filter, `(TransactionId, Id)` ordering, and the partial index.
3. Add `VisibleAt` and backoff on both sides; topology unchanged.
4. Head discovery, `SKIP LOCKED`, self-serving workers; remove partition discovery and batching. Both sides are still single-transaction here, so the system is fully working.
5. Outbox Lease, three-transaction model, completion guard. The delivery guarantee changes here.
6. Scheduled delivery on the message constructors.

Steps 4 and 5 are the real seam: everything through 4 is behaviour-preserving in kind, and 5 alone
is revertible if at-least-once outbox delivery causes trouble in practice.

## Testing Decisions

A good test here asserts on behaviour a consumer of the library could observe — which handler was
invoked, with what, in what order, and what the database rows look like afterwards. It does not
assert on which SQL was generated, how many round-trips occurred, or the internal state of the
worker pool. It does not sleep to make timing work out.

### Seams

`ProcessNextAsync` is the primary seam and should carry nearly all coverage: ordering within a
Group, Head selection, Settled behaviour, backoff, Lease expiry, the completion guard, and
poison-message stalling. It runs against real PostgreSQL through the existing Testcontainers base
class, and it is fully deterministic — no polling, no channels, no hosted services.

The database itself is the second seam: tests arrange and assert row state directly. Because all
timing is server-side, elapsed time is simulated by moving the column rather than by waiting —
setting `VisibleAt` into the past expires a Lease or completes a backoff instantly. Keep one or two
genuinely time-based tests with sub-second durations to prove the wiring.

The message-writing path keeps its existing seam through `IOutbox.AddMessageAsync` inside a caller
transaction.

Two or three end-to-end tests through the hosted services remain, to prove dependency-injection
wiring and that multiple workers handle distinct Groups concurrently. Everything else moves down to
`ProcessNextAsync`.

### Removals

The test-only production hooks are deleted, not ported: the `protected virtual` "no messages found"
signal, the `internal virtual` start method, and the two `ConcurrentProcessor` subclasses in the
test project that exist to override them. They were compensating for the nondeterminism of the
polling loop, which `ProcessNextAsync` removes at the source. Existing tests that spin-wait against
static handler collections should be rewritten to drive `ProcessNextAsync` directly.

### Prior art

The Testcontainers-backed base class, the per-test service-collection setup, the static collections
on test handlers used as the assertion surface, and the xunit collection fixtures that isolate them
are all established patterns in the test project and should be reused as-is.

### Coverage worth calling out

Ordering under concurrent producers to one Group is the reason ADR 0002 exists and needs a test that
interleaves two open transactions. The completion guard needs a test where a Lease is expired and
re-claimed before the original worker completes. Poison-message stalling needs a test proving later
messages in the Group are not handled, and that other Groups are unaffected.

## Out of Scope

Dead-letter handling. A permanently failing message stalls its Group by design (ADR 0004); a
configurable dead-letter mechanism is a deliberate follow-up. `Discard()` exception policies remain
the way to drop a known-bad message in the meantime.

Cross-instance wake-up. `pg_notify` on commit with a `LISTEN` subscription per instance is a
follow-up; it needs a dedicated long-lived connection and a reconnect story, and it is pure latency
optimisation with no correctness overlap. Polling remains the wake-up mechanism.

OpenTelemetry metrics and traces. The Lease-lost warning and stalled-Group detection are described
here as behaviour, but emitting them as instruments is separate work.

PostgreSQL declarative table partitioning of the message tables, and anything requiring
`pg_partman`.

A batch-aware handler interface that receives many messages in one call. This is a genuinely
different feature, not precluded by ADR 0003.

pgmq's alternative group-read strategies — the throughput-optimised and round-robin variants — and
its archive tables. Handled messages continue to be marked and swept, not moved.

Attempt counting on claim rather than on failure. A worker killed mid-handler records no attempt;
this is accepted, and the reasoning is in ADR 0004.

## Further Notes

This adopts pgmq's semantics without its SQL. pgmq stores the group in a JSONB header, so every one
of its grouped reads is a full table scan, and the GIN index it recommends for FIFO performance does
not in fact serve any of its own FIFO queries. Because `GroupKey` is a real column here, a plain
partial B-tree index turns Head discovery into an index-ordered lookup. The behaviour to match is
pgmq's `read_grouped_head`; the query shape should not be copied.

Two behaviours will look like defects to anyone reading this code later, which is why both have
ADRs. A Group wedged behind a poison message is deliberate (ADR 0004). Delivery stalling because an
unrelated long-running write transaction is holding the snapshot minimum back is inherent to the
Settled rule (ADR 0002); read-only transactions do not cause it, since they are assigned no
transaction id. This is the same coupling logical replication and CDC have.

The Lease is weaker than the row lock it replaces on the outbox: a row lock is enforced by the
database and dies with the connection, whereas a Lease is advisory and outlives a dead worker. That
is accepted in exchange for short transactions, and the completion guard is what limits the damage.

The README is wrong in four places once this lands: it documents `FOR UPDATE NOWAIT` locking,
savepoint-based batch error handling, partition terminology, and an unqualified ordering promise.
