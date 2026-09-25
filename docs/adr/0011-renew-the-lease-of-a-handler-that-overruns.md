# The Lease of a Handler that overruns its timeout is renewed

An outbox Lease runs for `HandlerTimeout` plus a 15-second margin, and the timeout cancels the
Handler's token. That keeps a worker inside its Lease only if the Handler honours the token. .NET
cannot stop a Task that ignores it. So a Handler that ignored the token used to run past its Lease
while a second worker claimed the message and ran it too. Once the second worker completed the
message, the next message in the Group could run while the first Handler was still inside the one
before it. That broke per-Group ordering, not just at-least-once delivery.

Once `HandlerTimeout` has passed and the Handler has not returned, the worker now renews its Lease
every third of the margin. Each renewal sets `visible_at` to the database clock plus the margin,
guarded on the Lease it currently holds. The renewal uses its own connection, because the Handler
may be using the scoped DbContext at that moment. The new instant goes to the outcome writes, so
their guard still matches. Renewal stops when the Handler returns, when the Lease turns out to belong
to another worker, or when the process dies. In the last case the Lease expires as before.

Rejected: no longer waiting for the Handler at the deadline. That frees the worker but not the
Handler, so it keeps the overlap and also disposes the scope the Handler is still using.

## Consequences

A Handler that hangs and ignores its token now blocks its Group until it returns, where it used to
be run again after the Lease expired. That trade matches ADR 0004: stalling a Group is preferred to
breaking its order. The stall shows up as a warning on every renewal.

A shutdown does not stop renewal either, since it does not stop that Handler.

`OutboxMessage.VisibleAt` has an internal setter rather than `init`, so the renewed instant can reach
the guarded writes.
