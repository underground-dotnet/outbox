# 06: Two-stage Head discovery

**What to build:** A Group offers only its Head, and it offers nothing at all when that Head is not
yet visible. This is the change that makes ordering survive backoff. The naive query — filter by
visibility, then take the oldest — hands out the second message of a Group whose first message is
waiting to be retried, which is exactly the reordering this work exists to prevent. It compiles,
and it passes any test that does not involve a failed message.

The Head must therefore be determined **without regard to visibility**, and visibility tested
against that one message afterwards. The feature spec alongside this ticket carries the query shape
and the three constraints that make it correct; follow it closely. This is the single most likely
place in the whole feature for a defect to hide.

**Blocked by:** 04 (retry backoff via a visibility instant).

**Status:** ready-for-agent

- [ ] A Group's Head is its oldest settled unhandled message, determined without applying any visibility filter.
- [ ] Visibility is tested against the Head alone. A Group whose Head is not yet visible contributes nothing.
- [ ] Row locking uses skip-locked semantics, so one busy Group no longer aborts the query for every other Group.
- [ ] The lock-unavailable error path previously used as flow control is removed.
- [ ] A test proves that while a Group's Head is in backoff, no later message in that Group is handled, however long it has waited.
- [ ] A test proves other Groups continue to be handled while one Group's Head is unavailable.
- [ ] A test proves that once the Head becomes visible again it is handled before anything behind it.
