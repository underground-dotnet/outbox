# 11: Correct the documentation

**What to build:** The README currently describes a system that no longer exists and promises a
guarantee it does not keep. A reader should come away knowing the real delivery guarantees, the real
ordering guarantee and its caveats, and the two behaviours that look like defects but are deliberate.

**Blocked by:** 10 (outbox Lease and three-transaction delivery).

**Status:** ready-for-agent

- [ ] The README no longer describes lock-and-wait fetching, savepoint-based batch error handling, batch size, or partition terminology.
- [ ] It states plainly that outbox delivery is at-least-once and that outbox handlers must be idempotent, and that inbox delivery is exactly-once.
- [ ] It qualifies the ordering guarantee as transaction-start order, and notes that a long-running write transaction anywhere in the database delays delivery.
- [ ] It documents that a permanently failing message stalls its own Group by design, and points at the relevant decision record.
- [ ] The retention setting is documented on the inbox as the duplicate-suppression window, since shortening it silently starts accepting duplicate events.
- [ ] It states the minimum supported PostgreSQL version implied by the transaction-identifier type.
- [ ] The configuration reference lists the new settings and omits the removed one.
