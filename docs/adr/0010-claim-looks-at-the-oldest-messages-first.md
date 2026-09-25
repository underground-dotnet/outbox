# The claim looks at the oldest pending messages before it considers every group

Claiming used to compute every group's head message and then take the oldest one that could be
claimed. Computing every head reads every pending row, so each claim cost time proportional to the
whole backlog, and a claim yields one message (ADR 0003). On Postgres 18, 200k pending rows in 20k
groups cost about 140 ms per claim, and 200k groups of one message about 400 ms. A backlog therefore
drained more slowly the larger it was, which is worst straight after an outage.

A claim is now two statements with one answer:

1. **Within a window.** Walk the pending messages oldest first, `(transaction_id, id)`, among the
   1000 oldest stable ones, and stop at the first that is visible, not locked, and the head of its
   group. Testing "head of its group" is one probe of the group index. This costs the same however
   long the backlog is: about 0.5 ms when it finds something, and at most about 7 ms when it does
   not.
2. **Across all groups.** Only when the window offers nothing, run the previous full query.

The answer is the same as before, not an approximation. The window holds every stable pending message
up to its last one, so if any message in it can be claimed, the oldest one that can be claimed anywhere
is in it as well. Ordering, stability (ADR 0002) and `SKIP LOCKED` behave exactly as before.

## Consequences

Both tables gain a partial index on `(transaction_id, id) WHERE completed_at IS NULL`.
It is one more index to maintain on insert, and it does not change what makes
an update HOT, because `completed_at` is already in an index predicate.

The new index makes the planner prefer, for the full query, a plan that walks it and tests every row
against every head, which is quadratic. The full query therefore materialises the heads and sorts on
their own columns. Both statements keep the window's end and the head test in forms the planner
cannot turn into a scan of the whole table; the comments on the SQL say which forms and why.

The window does not help when nothing near the front can be claimed: a group stalled by a poison
message (ADR 0004) with a thousand or more messages behind it, or every group backing off at once.
Every claim then pays for both statements, which is the old cost plus a few milliseconds. The
lasting remedy for the first case is to take a stalled group out of the pending rows, which is a
dead-letter concern rather than a claim one.

Two alternatives were measured and rejected. A recursive-CTE skip scan over `group_key` visits each
group once but costs about 400 ms at 20k groups, because a recursive CTE is expensive per iteration.
A table holding each group's head would avoid the fallback entirely, but every insert would have to
update its group's row, which serialises concurrent writers to one group, and it would have to track
heads correctly while transactions that could still insert earlier messages are open.
