# 03: Ordering that survives concurrent producers

**What to build:** Messages in a Group are handled in the right order even when two of the
application's own transactions write to that Group at the same time. Today the identity column
decides order, and identity values are handed out at insert time rather than commit time, so a
transaction that starts later but commits first has its message handled first, silently. A message
becomes eligible only once it is Settled: once no still-running transaction could yet insert an
earlier message into its Group.

**Blocked by:** 02 (single-step processing operation).

**Status:** ready-for-agent

- [ ] Every message records the identifier of the transaction that inserted it, defaulted by the database.
- [ ] Wherever order matters, messages are ordered by transaction identifier and then by insertion identifier. Ordering by insertion identifier alone stays incorrect even once the eligibility rule is in place.
- [ ] A message is not offered for handling until no still-running transaction could insert an earlier message into its Group.
- [ ] A partial index over unhandled messages serves Head lookup, so its cost scales with the number of Groups rather than the number of rows.
- [ ] A test interleaves two open transactions writing to one Group, commits them in the opposite order to which they started, and proves the messages are handled in transaction-start order.
- [ ] A test proves a message written by a still-open transaction is not handled, and is handled once that transaction commits.
