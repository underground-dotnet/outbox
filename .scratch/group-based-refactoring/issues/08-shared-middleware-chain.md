# 08: Shared per-message chain

**What to build:** The work done to one message — tracing, exception policy, backoff computation,
savepoint, dispatch — is expressed once as an ordered chain used by both the inbox and the outbox,
so that a change to any of it cannot be applied to one side and forgotten on the other. Nothing
behaves differently. This is a prefactor for the outbox Lease work, and it lands here rather than
earlier because the shape of one step is not settled until 07.

**Blocked by:** 07 (workers claim one Head at a time).

**Status:** ready-for-agent

- [ ] Per-message concerns are expressed as a single ordered chain rather than inline in the processing loop.
- [ ] Both sides use the same chain. The savepoint stage is simply absent from the side where no transaction is open.
- [ ] The chain is assembled by an internal factory and is not publicly configurable, because the order between stages is a correctness property rather than a preference.
- [ ] Transaction boundaries, claiming a Head, and writing the outcome remain outside the chain.
- [ ] The existing test suite passes with no behavioural change.
