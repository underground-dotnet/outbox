---
status: proposed
---

# One database hosts several named Inboxes

A modular monolith has several Features in one database, each wanting exactly-once delivery of the same
event in a transaction of its own Feature's `DbContext`. A single inbox cannot give that: EventId is
unique across the table, so a second Feature's row for the same event is a duplicate; the Handler
Registry admits one Handler per type; and `AddInboxServices` registers against non-generic types, so a
second call overwrites the first and only one context can run the inbox transaction.

So an Inbox is now named, and the name is required. Rows carry it in a `NOT NULL` column with no default,
EventId is unique per Inbox, Groups are scoped to their Inbox, and each Inbox has its own Handler
Registry, processor, workers and context: `AddInboxServices<PaymentsContext>(inbox: "Payments")`. A
generated `Add<Assembly>MessageHandlers(inbox: ...)` binds an assembly's inbox Handlers to one Inbox.

Staging broadcasts: `IInbox.StageMessage` writes one row into every Inbox whose Handlers handle the
message's type, and throws when none does. Who handles a type is known in one place, so a caller cannot
enumerate recipients, even by accident.

## Considered options

**Keep one inbox and let the application work around it** — derived EventIds per recipient, a wrapper
message type per recipient, and the recipient's context enlisted onto the inbox's connection with
`UseTransaction`. Every consumer of the library would rebuild that plumbing, and get it wrong silently.

**Bind Handlers to an Inbox by attribute.** Repeats a string on every Handler class; one assembly maps
naturally to one Inbox.

## Consequences

Breaking: the column, the unique index moving to `(inbox, event_id)`, and the required name. An external
producer writing rows directly must name the Inbox; forgetting fails its `INSERT` rather than leaving a
row nobody claims.

Workers are per Inbox, so one Inbox's backlog never takes workers from another, at the cost of
connections per Inbox. Every Inbox's write transaction still pins the shared watermark (ADR 0002), so a
slow Handler in one Inbox delays discovery in all of them.

The outbox stays single. The same change removes the DI obstacle to several outboxes, but nothing needs
them yet.
