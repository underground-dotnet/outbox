# Outbox Library

`Underground.Outbox` is a .NET library for the transactional outbox and inbox patterns on top of EF Core and PostgreSQL.

It stores messages in the same database transaction as your business changes, then processes them in the background. The library is group-aware, can run on multiple application instances, and supports push-triggered processing through `IOutbox.ProcessMessages()`.

## How it works

Every message belongs to a **group**, identified by its `GroupKey`. A group offers only its **head** — its oldest **settled** message that has not yet been handled, where settled means no still-running transaction could yet insert an earlier one into that group (see [Ordering](#ordering)). A group whose head is not yet visible — because it is scheduled for later, because it is backing off after a failure, or because another worker currently holds it — offers nothing at all, rather than offering the message behind it.

Each worker runs one query that considers every group's head at once and claims the oldest of them it can lock with `FOR UPDATE ... SKIP LOCKED`. A head another worker already holds is skipped rather than waited for, so the claim falls through to the next group's head. The worker then handles that one message and claims again.

Nothing hands groups out to workers; the database distributes them. Messages of one group are therefore handled one at a time, in order, and different groups proceed concurrently.

One claim is one message. There is no batch and no batch size on either side — throughput comes from how many groups your application defines, not from how many messages fit in a fetch. See [ADR 0003](docs/adr/0003-no-batching.md).

The two sides differ in where the transaction boundary sits, because their handlers do genuinely different things:

- An **inbox** handler applies an externally-originated event to this same database. One transaction spans the claim, the handler, and the write that records the message as handled.
- An **outbox** handler causes an effect *outside* this database — an HTTP call, a Kafka publish — whose latency we do not control, and holding a Postgres transaction open across that is what this design exists to avoid. The worker takes a time-bounded **lease** in a short transaction, commits it, dispatches with nothing open, and records the outcome in a second short transaction.

See [ADR 0001](docs/adr/0001-split-transaction-model-between-inbox-and-outbox.md).

## Delivery guarantees

**Outbox delivery is at-least-once. Outbox handlers must be idempotent.**

A lease is time-bounded, which is what stops an instance that dies mid-delivery from blocking its group forever: the lease expires on its own and the message is offered again. The price is that a worker which died — or merely overran — after the external effect but before the completion write causes that effect to happen twice. A worker that finishes after its lease expired detects the loss, logs a warning, and discards its outcome, so the message is not fanned out any further.

**Inbox delivery is exactly-once.** The handler's writes and the record that the message was handled commit in the same transaction, so either both happen or neither does.

## Ordering

Messages within a group are handled strictly in order — but that order is **the order in which the writing transactions started**, not the order in which the rows were appended.

An identity value is assigned when a row is inserted, not when its transaction commits, so ordering by `id` alone lets a transaction that started later but committed first have its message handled first. Every message therefore carries a `TransactionId`, messages are ordered by `(TransactionId, Id)`, and a message is offered only once it is **settled** — once no still-running transaction could yet insert an earlier message into its group. See [ADR 0002](docs/adr/0002-order-by-transaction-id-not-sequence.md).

Two consequences are worth knowing before you rely on this:

- **A long-running write transaction anywhere in the database delays delivery.** The settled test is against the snapshot minimum, which an open write transaction holds back, so a long writer stalls *all* message delivery until it commits — not just delivery of its own group. Read-only transactions are unaffected, as they are assigned no transaction id. This is the same coupling logical replication and CDC have, and it needs monitoring.
- Messages appended within one transaction keep their relative order.

## Two behaviours that look like defects

Both are deliberate, and knowing about them up front is cheaper than discovering them in production.

**A permanently failing message stalls its own group, forever.** It is retried with an exponential backoff up to a ten-minute ceiling and never given up on, and every message behind it in that group waits. Other groups are unaffected. Under strict per-group ordering the alternative — skipping past it — silently drops a message out of an ordered stream, which is worse for a consumer that depends on the order. Detection is external: a group that stops draining shows up as unbounded growth in unhandled messages. See [ADR 0004](docs/adr/0004-poison-messages-block-their-group.md). Until a dead-letter mechanism exists, `Discard()` exception policies are the way to drop a known-bad message.

**A scheduled or backing-off message holds back everything behind it in its group.** A group offers only its head, so nothing written after a message scheduled for tomorrow is handled until that message has been. Give a message its own group key if the delay is meant to apply to it alone.

## Features

- **EF Core based**: built on top of EF Core abstractions and DbContexts.
- **Outbox and inbox support**: both sides share the claim model and the per-message chain, and diverge only where their transaction models genuinely differ.
- **Push-triggered processing**: you can call `IOutbox.ProcessMessages()` to schedule a run immediately after commit. You can use a dbcontext interceptor to automate this.
- **Background processing**: hosted services also schedule processing runs on a configurable delay.
- **Group-aware parallelism**: different groups are handled concurrently.
- **Multi-instance safe**: multiple servers can process the same table without duplicating work under normal operation.
- **Total ordering within a group**: ordering holds even when two of your own transactions write to the same group concurrently.
- **Scheduled delivery**: a message can be given the instant from which it may be handled.
- **Retry backoff**: failed messages are retried with an exponential, jittered delay computed by the database.
- **Bounded handler runtime**: every handler is cancelled once `HandlerTimeout` elapses, so a hung external call cannot occupy a worker for good.
- **Retention cleanup**: processed messages are deleted automatically after a configurable retention period.
- **Source generation**: avoids runtime reflection for handler dispatch and DI wiring.

## Requirements

- .NET / EF Core application
- **PostgreSQL 13 or newer**, via `Npgsql`

PostgreSQL 13 is the floor because ordering depends on the 64-bit transaction identifier type `xid8` and on `pg_current_xact_id()`, `pg_current_snapshot()` and `pg_snapshot_xmin()`, all of which arrived in that release. Claiming also relies on `FOR UPDATE ... SKIP LOCKED`.

## Getting started

### Installation

```bash
dotnet add package Underground.Outbox
dotnet add package Underground.Outbox.SourceGenerator
```

**Important**: The source generator package must be added to the root/main project where dependency injection is configured. Other referenced projects only need to import the main `Underground.Outbox` package.

### Configuration

1. **Add Services**: Configure the outbox services in your `Program.cs` file:

    ```csharp
    builder.Services.AddOutboxServices<AppDbContext>(cfg =>
    {
        cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
        cfg.AddHandler<ExampleMessageHandler, AnotherMessage>();
    });

    builder.Services.AddInboxServices<AppDbContext>(cfg =>
    {
        cfg.AddHandler<InboxMessageHandler, ExampleMessage>();
    });
    ```

2. **Adjust DbContext**: Add interfaces and message types to your DbContext. This ensures that you can use EF migrations to add the tables to your database.

    ```csharp
    sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
    {
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<InboxMessage> InboxMessages { get; set; }
    }
    ```

3. **Handle Messages**: Create message handlers by implementing `IInboxMessageHandler` and `IOutboxMessageHandler`.

### Add messages

Adding to the outbox requires an active database transaction. That is intentional: the outbox write must commit together with your business data.

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync();

await outbox.AddMessageAsync(
    dbContext,
    new OutboxMessage(
        Guid.NewGuid(),
        DateTime.UtcNow,
        new ExampleMessage("Hello, World!"),
        groupKey: "customer-123"),
    cancellationToken
);

await transaction.CommitAsync();
```

To schedule a message instead of delivering it as soon as possible, pass `visibleAt`:

```csharp
new OutboxMessage(
    Guid.NewGuid(),
    DateTime.UtcNow,
    new ReminderMessage("Your trial ends today"),
    groupKey: "customer-123",
    visibleAt: DateTime.UtcNow.AddDays(1));
```

## Message model

Both `OutboxMessage` and `InboxMessage` contain:

| Property | Description |
|----------|-------------|
| `EventId` | Unique event identifier. A unique index prevents duplicates for the same event id. |
| `TransactionId` | The identifier of the transaction that inserted the message, assigned by the database. Together with `Id` it is the sort key that makes ordering within a group total. |
| `CreatedAt` | When the message was written. |
| `Type` | CLR type name of the serialized payload. |
| `GroupKey` | Logical group used for concurrency and ordering. Defaults to `"default"`. |
| `Data` | Serialized message payload. |
| `RetryCount` | Number of failed processing attempts. |
| `VisibleAt` | The instant from which the message may be handled. Defaulted by the database to the present, pushed into the future by the retry backoff, and — on the outbox — set to the lease expiry while a worker holds the message. |
| `ProcessedAt` | Null until the message is completed successfully. |

Handlers also receive `MessageMetadata` with `EventId`, `GroupKey`, and `RetryCount`.

```csharp
using Underground.Outbox;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync( ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        var eventId = metadata.EventId;
        var groupKey = metadata.GroupKey;
        var retryCount = metadata.RetryCount;

        // Process the message
        return Task.CompletedTask;
    }
}
```

The `cancellationToken` a handler is passed is cancelled once `HandlerTimeout` elapses, and honouring it is what turns a hung call into an ordinary failed attempt. On the outbox the lease is that timeout plus a margin for the completion write, so the token always fires while the worker still holds the message.

## Push-based processing

This library supports push-based processing through `IOutbox.ProcessMessages()`.

That means the producer side can add messages, commit the transaction, and then trigger processing right away instead of waiting for the next scheduled cycle.

If you want this to happen automatically after `transaction.CommitAsync()`, register the built-in EF Core interceptor:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor>());
});
```

With that registration in place, a successful `transaction.CommitAsync()` call will trigger `IOutbox.ProcessMessages()` and/or `IInbox.ProcessMessages()` when new `OutboxMessage` or `InboxMessage` rows were inserted in that unit of work.

## Choosing group keys

Groups are the unit of both ordering and concurrency, and the only source of parallelism.

Use the group key to group messages that must stay ordered relative to each other, for example per aggregate, account, or customer. Messages that have no ordering relationship belong in different groups.

The default group key is `"default"`. Leaving it there puts every message in one group, which means strictly serial handling and no parallelism at all — and it means one permanently failing message stalls everything.

`MaxConcurrentGroups` is the number of workers, and with it the ceiling on how many groups are in flight at once. `1` means strictly serial handling across all groups — one message anywhere in the system at a time — rather than one message per group.

## Multiple servers

The library can run on multiple servers against the same database, with no global distributed lock and no coordination between instances.

The claim query is what keeps two workers apart: one worker locks the head it claims, another worker or server running the same query finds that row locked and skips it rather than waiting, and so ends up on a different group.

How long that separation lasts differs by side:

- On the **inbox**, the row lock is held for the whole transaction, which spans the handler. Nothing else can touch the message until it is done, and the lock dies with the connection if the instance does.
- On the **outbox**, the claim transaction is short and the lock is gone once it commits. What keeps other workers off the message during dispatch is the lease: the claim sets `VisibleAt` to the lease expiry, so the message is out of sight for as long as this worker has to finish and comes back on its own if the worker never does.

Under normal operation two workers therefore never handle the same message at the same time. The exception is an outbox lease that expires while its worker is still running, which is the deliberate cost of not holding a transaction across an external call — see [Delivery guarantees](#delivery-guarantees).

## Error handling

When a handler throws, the message's `RetryCount` is incremented and its `VisibleAt` is pushed into the future by the retry backoff. The message is now out of sight, so its group offers nothing until the backoff elapses, while every other group carries on.

On the inbox the handler runs inside a savepoint, so its writes are rolled back while that bookkeeping still commits with the surrounding transaction. On the outbox there is no transaction open during dispatch and so no savepoint: an outbox handler's business is an effect outside this database, and one that also writes to this database is asking for the inbox.

The backoff doubles with every failed attempt — `BackoffBase`, then twice that, and so on — up to `MaxBackoff`, and each delay is varied by `BackoffJitter` either way so that groups which all failed against one shared dependency do not retry in lockstep. The new `VisibleAt` is computed by PostgreSQL from `clock_timestamp()`; the application only ever supplies the interval, so an instance with a skewed clock cannot retry early or late.

A handler that exceeds `HandlerTimeout` is cancelled and recorded as a failed attempt like any other, rather than occupying its worker indefinitely.

You can configure exception policies per handler registration, or globally for all inbox and outbox handlers. To discard a message for a specific exception type, chain `OnException<TException>().Discard()` from `AddHandler`:

```csharp
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();

    cfg.AddHandler<ExampleMessageHandler, SecondMessage>()
        .OnException<InvalidOperationException>()
        .Discard();

    cfg.Policies.OnException<DataException>()
        .Discard();
});
```

`Discard()` deletes the failed message from the outbox or inbox table instead of leaving it available for retry. Exception policies can be scoped to a specific handler and message type registration, or configured globally through `cfg.Policies`. If both global and registration-specific policies match, the registration-specific policies win.

If no matching exception policy exists, the failed message stays in the table with an incremented `RetryCount` and is retried once its backoff has elapsed — forever, stalling its group, per [ADR 0004](docs/adr/0004-poison-messages-block-their-group.md).

## Cleanup and retention

Processed inbox and outbox messages are not kept forever.

- `ProcessedMessageRetention` controls how long successfully processed messages are retained.
- `CleanupDelaySeconds` controls how often the cleanup hosted service runs.

Cleanup deletes rows where `ProcessedAt` is older than the configured retention cutoff.

**On the inbox, `ProcessedMessageRetention` is your duplicate-suppression window.** A redelivered event is rejected because its `EventId` already exists in the inbox table — and that only works while the row is still there. Once cleanup has deleted it, the same event is accepted again and handled a second time. Set the retention to at least the longest window over which the systems that send you events might redeliver one; shortening it does not fail loudly, it silently starts accepting duplicates.

The same setting on the outbox is only about table size: nothing else reads a processed outbox row.

## Configuration reference

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentGroups` | `4` | Number of workers, and with it the number of groups that can be handled concurrently. `1` means strictly serial handling across all groups. |
| `HandlerTimeout` | `45 seconds` | Time a handler is given before its cancellation token fires and the attempt is recorded as failed. The outbox lease is derived from this plus a margin for the completion write, and is deliberately not configurable on its own: a lease shorter than the timeout would guarantee double delivery on every slow message. |
| `BackoffBase` | `1 second` | Delay before a message that failed for the first time is offered again. |
| `MaxBackoff` | `10 minutes` | Ceiling the doubling retry delay stops at. Jitter is applied afterwards, so an actual delay may exceed this by the jitter proportion. |
| `BackoffJitter` | `0.2` | Proportion each retry delay is randomly varied by, either way. `0` gives exact delays. |
| `ProcessingDelayMilliseconds` | `4000` | Delay between scheduled processing cycles. |
| `ProcessedMessageRetention` | `7 days` | How long processed rows are kept before cleanup. On the inbox this is also the duplicate-suppression window. |
| `CleanupDelaySeconds` | `3600` | Delay between cleanup runs. |

## Example

```bash
dotnet run --project example/ConsoleApp/
```

## License

This project is licensed under the MIT License.
