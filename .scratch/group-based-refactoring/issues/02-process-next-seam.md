# 02: Single-step processing operation, and removal of test-only hooks

**What to build:** Handling one unit of work becomes a first-class operation: a caller asks the
processor to handle the next available message and is told whether there was any. The background
worker loop becomes a loop over that operation. This is the production decomposition the later
tickets need, and it is also what makes every subsequent behaviour testable without racing a
poller, so it lands before any behaviour changes.

**Blocked by:** 01 (adopt Group vocabulary). Sequencing rather than a functional dependency: both
touch the same processor types.

**Status:** ready-for-agent

- [ ] The processor exposes a single-step operation that handles at most one unit of work and reports whether it did any.
- [ ] The background worker loop is expressed in terms of that operation.
- [ ] The overridable "no messages found" notification, the overridable start method, and the two processor subclasses in the test project that exist to override them are deleted.
- [ ] No production type retains a member whose only purpose is to support tests.
- [ ] Tests that previously waited on a spin-loop against static handler collections drive the single-step operation directly and assert without timeouts.
- [ ] Two or three end-to-end tests through the hosted services remain, covering dependency-injection wiring and concurrent handling of distinct Groups.
