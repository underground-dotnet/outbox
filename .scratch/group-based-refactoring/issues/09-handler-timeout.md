# 09: Handler timeout

**What to build:** A handler that never returns no longer occupies a worker indefinitely or holds a
transaction open. Every handler gets a bounded amount of time, and its cancellation token fires when
that time is up.

**Blocked by:** 08 (shared per-message chain).

**Status:** ready-for-agent

- [ ] A configurable maximum handler duration applies to both inbox and outbox handlers.
- [ ] The cancellation token passed to a handler is cancelled once that duration elapses.
- [ ] A handler cancelled this way is treated as a failed attempt and backs off like any other failure.
- [ ] The timeout is a stage in the shared chain rather than duplicated per side.
- [ ] A test proves a handler that never returns is cancelled, its message backs off, and the worker goes on to other work.
