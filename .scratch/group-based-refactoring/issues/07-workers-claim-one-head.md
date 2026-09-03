# 07: Workers claim one Head at a time

**What to build:** Workers stop being fed work by a discovery stage and start serving themselves.
Each worker claims one Head, handles it, and repeats until nothing is available. The database
distributes Groups across workers through skip-locked semantics, with no coordination and no
lock-failure signalling. Batching goes at the same time: throughput comes from Groups, not from how
many messages fit into one fetch.

**Blocked by:** 06 (two-stage Head discovery).

**Status:** ready-for-agent

- [ ] Each worker independently claims one Head, handles it, and repeats until a claim returns nothing, then waits for a trigger or the poll delay.
- [ ] The separate work-discovery stage and the channel that fed Group names to workers are removed.
- [ ] Batch size is removed from configuration. One message is handled per claim, on both sides.
- [ ] The number of workers is governed by the concurrent-Groups setting, and it is documented that a value of one means strictly serial handling across all Groups.
- [ ] The push trigger continues to wake workers immediately after a commit.
- [ ] The savepoint is retained on the inbox, isolating a failed handler's writes from the attempt bookkeeping so that both still commit together.
- [ ] A test proves distinct Groups are handled concurrently by different workers.
- [ ] A test proves a slow handler in one Group does not delay any other Group.
