# Inbox Messages fan out to one row per Recipient, and a Group belongs to one Recipient

An event that several inbox Handlers must apply used to be impossible: `event_id` was unique and the
Handler Registry allowed one Handler per message type, so the only way to reach two was one Handler
fanning out in-process, where one failing rolled back and retried the other. Each Handler should
apply the event in its own transaction instead. So Staging or Adding an Inbox Message now writes one
row per inbox Handler of its type, each addressed to that Handler as its **Recipient**, and the
Registry dispatches by (type, Recipient). This changes ADR 0008's one-Handler-per-type rule for the
inbox only; the outbox has no Recipient, so two Handlers there are still the contradiction.

## Considered options

**The caller fans out, passing a key such as a feature name.** Every caller would rebuild the same
loop, and the key is a string to keep in step with the Handler. The Registry already knows every
Handler once the container is built, so the library fans out and the Recipient is the Handler's
`Type.FullName`.

**Rewriting the `GroupKey` to `"{recipient}/{groupKey}"`.** No SQL change, but the rewritten key
leaks into `MessageMetadata`, logs and `messaging.destination.partition.id`.

## Consequences

**A Group is (Recipient, `GroupKey`).** Fan-out copies one `GroupKey` onto every Recipient's row;
grouping by key alone would let one Recipient's poison message (ADR 0004) stall its siblings'
copies, which is the coupling this exists to remove. The head test, `DISTINCT ON` and the partial
indexes all carry `recipient`.

**Uniqueness is (`event_id`, `recipient`), and Recipients are fixed when an event is first added.**
Adding an event again still throws `DbUpdateException`, even if a Handler deployed since would be a
new Recipient: that Handler never receives the event. A type with no inbox Handler throws at Stage
time rather than storing a row nobody can claim.

**`recipient` is `NOT NULL`, a breaking migration.** Pending rows must be backfilled with their
type's single Handler before upgrading:

```sql
UPDATE inbox SET recipient = '<Handler FullName>' WHERE type = '<message FullName>' AND completed_at IS NULL;
```

**Renaming or deleting a Handler orphans the rows addressed to it.** They fail with
`ParsingException` and retry indefinitely, stalling only that Recipient's Groups. Removing them is
the operator's call, made deliberately:

```sql
DELETE FROM inbox WHERE recipient = '<old Handler FullName>' AND completed_at IS NULL;
```
