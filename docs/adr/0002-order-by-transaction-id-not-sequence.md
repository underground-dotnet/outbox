# Order messages by transaction id, not by sequence

A `bigint` identity is assigned when a row is inserted, not when its transaction commits, so two
producers writing to the same group concurrently can have their messages handled in the wrong
order: the later-starting transaction commits first, its message is handled, and the earlier
message then appears behind it. Ordering by `id` alone cannot detect this.

Every message therefore carries `transaction_id xid8 DEFAULT pg_current_xact_id()`, messages are
ordered by `(transaction_id, id)`, and no message is offered for handling until
`transaction_id < pg_snapshot_xmin(pg_current_snapshot())` — that is, until no older transaction
could still insert ahead of it.

## Consequences

Ordering now reflects the order in which transactions *started*, not the order in which messages
were appended. Messages appended within one transaction keep their relative order.

Delivery latency is coupled to the oldest running **write** transaction in the database: a
long-running writer holds the snapshot minimum back and stalls all message delivery until it
commits. Read-only transactions are unaffected, as they are assigned no transaction id. This is
the same coupling logical replication and CDC have, and it needs monitoring.

**The inbox is itself one of those writers.** Per ADR 0001 an inbox worker holds a single
transaction across claim, Handler and outcome, and it is a *write* transaction from the claim
onwards: `SELECT ... FOR UPDATE` records the locker in the tuple's `xmax`, which requires a real
transaction id. So for as long as a Handler runs, that worker's id is a floor under
`pg_snapshot_xmin` for every other session.

The watermark is database-wide, and the filter runs before `DISTINCT ON (group_key)`, so the stall
is not confined to the busy worker, to the inbox, or to that Handler's group: one slow inbox
Handler withholds every message newer than its claim from every worker on both sides. Under steady
load some worker is nearly always mid-Handler, which makes this a standing delivery-latency floor
of roughly the slowest concurrent inbox Handler rather than an occasional stall.

Neither half of that can be relaxed without giving something up. Shortening the inbox transaction
would cost the exactly-once delivery ADR 0001 exists to preserve, and the watermark cannot be
narrowed to a group because a running transaction's id says nothing about which group it will
insert into. The mitigation is therefore operational: keep inbox Handlers short, and monitor
delivery lag against the oldest running write transaction.

Ordering by `id` alone would remain incorrect even with the watermark in place, because a message
inserted earlier can belong to a transaction that started later and so is released later. The
sort key and the watermark must both be `transaction_id`.
