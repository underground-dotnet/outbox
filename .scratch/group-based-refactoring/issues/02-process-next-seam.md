# 02: Single-step processing operation, and removal of test-only hooks

**What to build:** Handling one unit of work becomes a first-class operation: a caller asks the
processor to handle the next available message and is told whether there was any. The background
worker loop becomes a loop over that operation. This is the production decomposition the later
tickets need, and it is also what makes every subsequent behaviour testable without racing a
poller, so it lands before any behaviour changes.

**Blocked by:** 01 (adopt Group vocabulary). Sequencing rather than a functional dependency: both
touch the same processor types.

**Status:** ready-for-agent

- [x] The processor exposes a single-step operation that handles at most one unit of work and reports whether it did any.
- [x] The background worker loop is expressed in terms of that operation.
- [x] The overridable "no messages found" notification, the overridable start method, and the two processor subclasses in the test project that exist to override them are deleted.
- [x] No production type retains a member whose only purpose is to support tests.
- [x] Tests that previously waited on a spin-loop against static handler collections drive the single-step operation directly and assert without timeouts.
- [x] Two or three end-to-end tests through the hosted services remain, covering dependency-injection wiring and concurrent handling of distinct Groups.

## Comments

`ProcessNextAsync` handles one Group's batch, not one message: batching and the Group-discovery
stage both live until 07. Its unit of work is therefore "the next waiting Group", and that is what
its return value reports — a Group that yields no messages still counts as taken, because returning
`false` there would abandon the Groups still queued behind it.

Discovery only runs when a processing run was scheduled (`ScheduleProcessingRun`, i.e. the push
trigger or the poll delay). Without that gate a Group whose Head keeps failing would be rediscovered
and retried in a hot loop, since there is no backoff yet. 04 makes the gate redundant and 07 removes
the queue it gates; both should delete it.
