# 10: Outbox Lease and three-transaction delivery

**What to build:** Outbox handlers stop holding a database transaction across a slow external call.
A worker takes a time-bounded Lease and commits it, dispatches with nothing open, then writes the
outcome in a separate transaction. A worker that dies no longer blocks its Group: the Lease simply
expires.

The cost is at-least-once delivery, which is why every write is guarded on the Lease that was
granted. Without the guard, a worker finishing after its Lease had expired would overwrite a newer
worker's claim and fan the message out to a third worker.

This is where the outbox delivery guarantee changes, and it is deliberately the last behavioural
change so that it can be reverted on its own.

**Blocked by:** 09 (handler timeout).

**Status:** ready-for-agent

- [ ] Claiming an outbox message commits a time-bounded Lease in its own transaction, before the handler runs.
- [ ] The outbox handler runs with no database transaction open.
- [ ] Success and failure are written in a separate transaction, each guarded on the Lease instant granted at claim time.
- [ ] A guarded write that affects no rows is reported as a lost Lease at warning level. It does not throw, and it does not mark the message handled.
- [ ] The Lease duration is derived from the handler timeout plus a margin for the completion write, and is not separately configurable, so a Lease shorter than the timeout is impossible to configure.
- [ ] The handler's cancellation fires before its Lease expires, so a message can never be taken from a live worker.
- [ ] Inbox handling continues to run entirely within one transaction and remains exactly-once.
- [ ] A test expires a Lease, re-claims the message as a second worker, then lets the first worker finish, and proves the first neither overwrites the second's claim nor marks the message handled.
- [ ] A test proves a message whose worker died becomes available again once its Lease expires.
