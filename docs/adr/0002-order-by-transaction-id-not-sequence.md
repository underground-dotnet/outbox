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

Ordering by `id` alone would remain incorrect even with the watermark in place, because a message
inserted earlier can belong to a transaction that started later and so is released later. The
sort key and the watermark must both be `transaction_id`.
