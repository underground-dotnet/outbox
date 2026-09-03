# 04: Retry backoff via a visibility instant

**What to build:** A handler that fails no longer retries immediately and forever. Every message
carries the instant from which it may be handled, and a failed attempt pushes that instant into the
future by an exponentially increasing, jittered, capped delay. A temporarily unavailable partner
system stops being hammered.

**Blocked by:** 03 (ordering that survives concurrent producers). Sequencing rather than a
functional dependency: the two columns are orthogonal, but both touch the same entity and fetch
query.

**Status:** ready-for-agent

- [ ] Every message carries the instant from which it may be handled, defaulted by the database to the present.
- [ ] A message whose instant lies in the future is not handled.
- [ ] A failed attempt sets that instant to an exponentially increasing delay based on the attempt count, with jitter, capped at a configured maximum.
- [ ] Base delay, maximum delay and jitter proportion are configurable.
- [ ] Delays are expressed as intervals and anchored by the database clock. The application never supplies an absolute instant, so a skewed application clock cannot affect timing.
- [ ] Tests simulate elapsed time by moving the stored instant into the past rather than by waiting.
- [ ] A test proves successive failures produce increasing delays and that growth stops at the cap.
