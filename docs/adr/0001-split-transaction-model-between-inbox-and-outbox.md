# Inbox and outbox use different transaction models

An inbox handler applies business logic to the same database, so it runs inside the transaction
that also marks the message handled: one transaction, exactly-once, rolled back to a per-message
savepoint on failure. An outbox handler talks to an external system (Kafka, HTTP) whose latency we
do not control, so holding a database transaction open across it is unacceptable; instead the
worker takes a time-bounded lease in its own short transaction, dispatches with no transaction
open, and completes in a second short transaction.

## Consequences

Outbox delivery is **at-least-once** and outbox handlers must be idempotent: a worker that dies
after the external effect but before completion will have the message redelivered when its lease
expires. Inbox delivery remains exactly-once. This asymmetry is deliberate and is the reason the
two sides cannot share a single processing loop.

The inbox transaction is a *write* transaction for its whole length, so it holds
`pg_snapshot_xmin` back and stalls head discovery for both sides while the handler runs — the
price of exactly-once, and the reason inbox handlers must stay short. See ADR 0002.

The outbox pays the opposite price and avoids that stall: its claim transaction commits in
milliseconds, so it barely moves the watermark.
