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

**Head Message**:
The oldest Stable message of a group that has not yet been Completed, where "oldest" means lowest
`(TransactionId, Id)`. A group offers only its head message for handling; if that message is not
yet visible, the group offers nothing at all.
_Avoid_: Head *(alone — the term names a message, not just a position)*, next message,
first message *(reads absolute; the head message moves as messages complete)*,
top message *(no agreed direction — top of a stack is newest, top of a queue is oldest)*, front

**Stable**:
A message whose place in its group's order can no longer change: its inserting transaction has
committed *and* no still-running transaction could yet insert an earlier message. Only Stable
messages are eligible to be a Head Message, which is what makes ordering within a group total
rather than approximate.
_Avoid_: Committed *(a committed row can still be ineligible — see ADR 0002)*,
Visible *(taken by VisibleAt, and a Stable message in backoff is not visible)*,
Settled, sequenced *(ADR 0002 uses "sequence" for the identity column)*, durable

**VisibleAt**:
The instant from which a message may be handled. One timestamp serves three roles: scheduled
delivery, retry backoff, and — on the outbox only — lease expiry.
_Avoid_: LockedUntil, vt, visibility timeout, NotBefore

**Claim**:
A worker holding one Head Message, to the exclusion of every other worker. The two sides hold it
differently — the inbox keeps the row lock its discovery statement took, the outbox commits a
Lease — and Claim is the name for what those two have in common.
_Avoid_: Reserve *(reservation is what Lease avoids being called)*, fetch *(reads as read-only;
claiming writes)*, checkout

**Lease**:
An outbox worker's time-bounded Claim on a message, taken by setting `VisibleAt` into the future
and released by Completing the message. It expires on its own, so a worker that dies never blocks
its group permanently. Advisory: nothing prevents a second worker acting after expiry, which is
why outbox delivery is at-least-once.
_Avoid_: Lock, reservation, checkout

**Handler**:
Application-supplied code that carries out the effect of one message. Never invoked concurrently
for two messages of the same group.
_Avoid_: Consumer, subscriber, listener

**Completed**:
A message whose Handler returned and whose outcome has been recorded, stamped `CompletedAt`. This
is the state of the *message*; the Handler is what ran, and the Processing Attempt is what
succeeded. A message that failed has not been Completed, however many attempts it has cost.
_Avoid_: Processed *(Processing now names the whole run, not its successful end)*, handled
*(shadows Handler)*, done, acked

**Processing**:
Everything one worker does to one message: the Claim, the run of the Middleware Pipeline, and the
write that records the outcome. Processing a message may succeed, fail, or discover the Lease was
lost; only the first Completes it.
_Avoid_: Handling *(that is what the Handler does, one step inside this)*, delivery, dispatch

**Processing Attempt**:
One run of Processing against a single Claimed Head Message, and what became of it: the message
Completed, a failure recorded against it, or the discovery that the Lease was lost and nothing
this worker did counted. A message accumulates one Processing Attempt per time it is offered,
which is what `RetryCount` counts.
_Avoid_: Attempt *(alone — an attempt at what?)*, context, envelope, result, outcome

**Partition**:
Reserved for PostgreSQL declarative table partitioning only. Never used for the logical grouping
that governs ordering and concurrency — that is a Group. The prohibition is on our own prose and
code; the one exception is at the wire, where a Group is emitted as OpenTelemetry's
`messaging.destination.partition.id`, because that is the attribute the semantic conventions define
for it. Deliberate, not an oversight.

**Creation Context**:
The trace context of the transaction that wrote a message, carried on the row so the worker that later
handles it continues the same trace instead of starting a new one. Written when the message is added and
never afterwards; a message written while nothing was tracing has none, and is handled under a trace of
its own.
_Avoid_: traceparent *(that is its encoding, and the column it lives in)*, correlation id, span context,
trace id *(only one half of it)*

**Middleware**:
One concern in the Processing of a single message — logging it, recording a failed attempt,
holding a savepoint, bounding how long its Handler may run — written as a wrapper around the rest
of that work. Middleware is ordered, and the order is a correctness property rather than a
preference.
_Avoid_: Stage, step, filter, interceptor, decorator

**Middleware Pipeline**:
The ordered Middleware both the inbox and the outbox run against one Claimed Head Message.
Everything done to a Claimed message belongs to the Middleware Pipeline, except the Claim and the
transaction boundary around it, which differ per side.
_Avoid_: Chain, middleware stack *(it is an ordered list composed outermost-first, not a LIFO
structure)*
