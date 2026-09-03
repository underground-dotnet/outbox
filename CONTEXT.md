# Underground.Outbox

A .NET library implementing the transactional outbox and inbox patterns on PostgreSQL, so that a
database change and the messaging that follows from it cannot disagree with each other.

## Language

**Outbox Message**:
A record of an intent to cause an effect *outside* this database, written in the same transaction
as the business change that justified it. Its handler is delivered **at-least-once** and must be
idempotent, because the effect cannot be rolled back with the database.
_Avoid_: Event, publication, outgoing message

**Inbox Message**:
A record of an externally-originated event to be applied *to* this database. Its handler runs in
the same transaction as the bookkeeping that records the message as handled, so it is applied
**exactly once**.
_Avoid_: Incoming message, consumed event

**Group**:
The set of messages that must be handled one at a time, in order, identified by `GroupKey`. Two
messages of the same group are never in flight simultaneously; two messages of different groups
may be. This is the unit of both ordering and concurrency, and the only source of parallelism.
_Avoid_: Partition, lane, stream, shard

**Head**:
The oldest settled message of a group that has not yet been handled, where "oldest" means lowest
`(TransactionId, Id)`. A group offers only its head for handling; if the head is not yet visible,
the group offers nothing at all.
_Avoid_: Next message, front

**Settled**:
A message whose inserting transaction has committed *and* behind which no still-running
transaction could yet insert an earlier message. Only settled messages are eligible to be a Head,
which is what makes ordering within a group total rather than approximate.
_Avoid_: Committed, visible, durable

**VisibleAt**:
The instant from which a message may be handled. One timestamp serves three roles: scheduled
delivery, retry backoff, and — on the outbox only — lease expiry.
_Avoid_: LockedUntil, vt, visibility timeout, NotBefore

**Lease**:
An outbox worker's time-bounded claim on a message, taken by setting `VisibleAt` into the future
and released by completing the message. It expires on its own, so a worker that dies never blocks
its group permanently. Advisory: nothing prevents a second worker acting after expiry, which is
why outbox delivery is at-least-once.
_Avoid_: Lock, reservation, checkout

**Handler**:
Application-supplied code that carries out the effect of one message. Never invoked concurrently
for two messages of the same group.
_Avoid_: Consumer, subscriber, listener

**Partition**:
Reserved for PostgreSQL declarative table partitioning only. Never used for the logical grouping
that governs ordering and concurrency — that is a Group.
