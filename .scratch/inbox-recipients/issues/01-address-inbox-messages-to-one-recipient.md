# Address every Inbox Message to one Recipient

Status: ready-for-agent

## What

Implement ADR 0012: Staging or Adding an Inbox Message writes one row per inbox Handler of its type,
each addressed to that Handler as its Recipient, and each Recipient has Groups of its own. Release as
0.22.0; `modulith-template` is blocked on it.

## Changes

- **Schema.** `InboxMessage` gains a required `recipient` column (text, `NOT NULL`). Unique index
  moves from `event_id` to (`event_id`, `recipient`). Both partial indexes from `MessageConfiguration`
  lead with `recipient` on the inbox (the outbox's stay as they are).
- **Fan-out.** `IInbox.StageMessage(s)` / `AddMessage(s)` expand each message into one row per
  Handler Entry registered for its `Type`, `recipient` = the Handler's `Type.FullName`. No inbox
  Handler for the type throws at Stage time.
- **Claim.** Inbox claim SQL treats a Group as (`recipient`, `group_key`): the head-message subquery,
  `DISTINCT ON`, and the index each query walks. The outbox claim is unchanged.
- **Registry.** `HandlerRegistry<InboxMessage>` holds many Handlers per message type and looks up by
  (type, Recipient). `HandlerRegistry<OutboxMessage>` keeps `CompetingHandlersException`. The
  generator's `OUTBOX001` applies to outbox Handlers only.
- **Dispatch.** `DispatchMessage` looks up by the row's Recipient; an unknown Recipient throws
  `ParsingException` as an unknown type does today. `MessageMetadata.GroupKey` stays the caller's key.
- **Telemetry.** Recipient as a span attribute `underground.outbox.recipient` and a log scope field.
- **Docs.** README: fan-out, the migration `UPDATE` and orphan `DELETE` from ADR 0012.

## Acceptance

- Two Handlers of one message type each get a row and are each Completed independently.
- One Handler's poison message does not delay the other Recipient's copy, nor later messages of the
  same `GroupKey` for that other Recipient.
- Ordering within (Recipient, `GroupKey`) holds as before.
- Adding the same `EventId` twice throws `DbUpdateException`.
- Staging a type with no inbox Handler throws.
- A row addressed to an unregistered Recipient fails and retries; other Recipients proceed.
- Two outbox Handlers of one type still fail the host at startup, and `OUTBOX001` still reports
  within one assembly; two inbox Handlers of one type do not.
- Generator snapshot tests cover an assembly declaring two inbox Handlers of one type.

## Comments

**Implemented.** One deviation: only the Group index leads with `recipient`, now
(`recipient`, `group_key`, `transaction_id`, `id`). The (`transaction_id`, `id`) index stays as it is: the
windowed claim and the probe beyond the window walk it oldest first across every Group (ADR 0010), which a
leading `recipient` would turn into a sort. The retention index on `completed_at` is likewise unchanged.
ADR 0012 is amended to say so.

Also fixed on the way: generated Handler Entries resolved their Handler through `IInboxMessageHandler<T>`,
which with two Handlers of one type ran the same one for both entries; they now resolve the concrete Handler.
