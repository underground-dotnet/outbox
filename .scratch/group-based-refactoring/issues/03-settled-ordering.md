# 03: Ordering that survives concurrent producers

**What to build:** Messages in a Group are handled in the right order even when two of the
application's own transactions write to that Group at the same time. Today the identity column
decides order, and identity values are handed out at insert time rather than commit time, so a
transaction that starts later but commits first has its message handled first, silently. A message
becomes eligible only once it is Settled: once no still-running transaction could yet insert an
earlier message into its Group.

**Blocked by:** 02 (single-step processing operation).

**Status:** done

- [x] Every message records the identifier of the transaction that inserted it, defaulted by the database.
- [x] Wherever order matters, messages are ordered by transaction identifier and then by insertion identifier. Ordering by insertion identifier alone stays incorrect even once the eligibility rule is in place.
- [x] A message is not offered for handling until no still-running transaction could insert an earlier message into its Group.
- [x] A partial index over unhandled messages serves Head lookup, so its cost scales with the number of Groups rather than the number of rows.
- [x] A test interleaves two open transactions writing to one Group, commits them in the opposite order to which they started, and proves the messages are handled in transaction-start order.
- [x] A test proves a message written by a still-open transaction is not handled, and is handled once that transaction commits.

## Comments

`TransactionId` is `ulong` mapped to `xid8`, defaulted with `pg_current_xact_id()`. Expressing that
default, and the partial index's `WHERE processed_at IS NULL` filter, needs model-building code
rather than data annotations, so both live in an internal `MessageConfiguration<TEntity>` that
reaches a consumer's model automatically through `[EntityTypeConfiguration]` on the two entity types
- no call the consumer has to remember. The `(ProcessedAt, GroupKey)` index it replaces existed for
the same lookup and is gone.

Two things about that configuration are easy to get wrong. It addresses its properties by name
rather than by lambda, because a lambda over `TEntity` reaches the members through `IMessage` and EF
then re-identifies the property from the interface's `PropertyInfo`, where the `[Column]` attribute
is not - which silently renamed the `id` and `group_key` columns to `Id` and `GroupKey`. And the
index filter is raw SQL naming a column, which cannot be read back off the model there either: an
`IEntityTypeConfiguration` runs before the mapping annotations are applied, so `GetColumnName()`
returns `ProcessedAt` at that point. The name is therefore a shared constant, used both by the filter
and by the entity's own `[Column]` attribute.

The ordering test inserts the *later* transaction's message first, so the identity order and the
transaction order disagree. Without that inversion the test passes just as well under the old
`ORDER BY id` and proves nothing. In the settled test the load-bearing assertion is the *committed*
message being withheld; the uncommitted one is invisible to any reader anyway. Both tests were
confirmed to fail against the previous query.

Group discovery (`FetchGroups`) still filters on `ProcessedAt` alone, so a Group holding only
unsettled messages is discovered and then yields nothing. That costs a poll interval of latency, not
correctness - no unsettled message is ever offered - and 07 removes the stage.

The README's claim that messages are ordered by `id` was corrected, since this change makes it
false. The rest of what 11 lists there is untouched.
