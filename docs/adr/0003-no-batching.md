# Messages are handled one at a time, not in batches

A worker claims a single message — the head of one group — handles it, and completes it. There is
no batch size and no multi-message fetch on either the inbox or the outbox.

Batching was removed rather than kept because it buys little and costs a lot here. On the outbox
there is no long transaction left to amortise, so a batch would save only database round-trips
while making the lease cover N external calls instead of one: with a 45-second lease, five
sequential HTTP calls give each one nine seconds before another worker begins double-delivering.
It would also require expressing partial-batch outcomes (some handled, some failed, the remainder
explicitly released). On the inbox a batch would save transactions, but a batch may only ever be
the *contiguous visible prefix* of a group — a message in backoff blocks everything behind it —
which is a subtle rule to implement and an easy one to get quietly wrong.

Throughput comes from Groups. Different groups are handled concurrently by independent workers, so
scaling is a matter of how many groups the application defines, not how many messages fit in one
fetch. Applications that put every message in one group get no parallelism, which is already
documented as the thing to avoid.

## Consequences

The savepoint remains, despite there being no batch: on the inbox it isolates a failed handler's
writes from the `RetryCount` and `VisibleAt` bookkeeping so both still commit together. What was
removed is per-message isolation *within* a batch, not savepoints as such.

A batch-aware handler interface (one call, many messages) would be a genuinely different feature
and is not precluded by this.
