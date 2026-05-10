# Outbox Library

`Underground.Outbox` is a .NET library for the transactional outbox and inbox patterns on top of EF Core and PostgreSQL.

It stores messages in the same database transaction as your business changes, then processes them in the background. The library is partition-aware, can run on multiple application instances, and supports push-triggered processing through `IOutbox.ProcessMessages()`.

## How it works

There are usually two common transactional outbox approaches:

### Using multiple transactions

The outbox processor is fetching a batch of outbox messages and marks them as `in processing` by updating a column in the outbox table. Then using a second transaction to process the messages.

Using this approach you have short lived transactions, but you need to deal with cases of releasing the lock on the rows when a processor fails.

### Using one transaction

The outbox processor is fetching a batch of outbox messages (sometimes with the addition of `SELECT FOR UPDATE SKIP LOCKED`) and uses the same transaction to process the messages.

This ensure that updating the messages table as well as any business logic tables is done in the same transaction (all or nothing). For larger batches it increases the risk of long running transactions.

For this library the single transaction approach was chosen. Messages in a batch will be processed until processing of one message fails.

## Features

- **EF Core based**: built on top of EF Core abstractions and DbContexts.
- **Outbox and inbox support**: both sides use the same processing model.
- **Push-triggered processing**: you can call `IOutbox.ProcessMessages()` to schedule a run immediately after commit. You can use a dbcontext interceptor to automate this.
- **Background processing**: hosted services also schedule processing runs on a configurable delay.
- **Partition-aware parallelism**: different partitions can be processed concurrently.
- **Multi-instance safe**: multiple servers can process the same outbox table without duplicating work.
- **Savepoint-based error handling**: failed messages are rolled back without undoing previously successful messages in the same batch.
- **Retention cleanup**: processed messages are deleted automatically after a configurable retention period.
- **Source generation**: avoids runtime reflection for handler dispatch and DI wiring.

## Requirements

- .NET / EF Core application
- PostgreSQL via `Npgsql`

The current implementation relies on PostgreSQL row locking with `FOR UPDATE NOWAIT` when fetching messages.

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
        cfg.AddHandler<InboxMessageHandler, InboxMessage>();
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
        partitionKey: "customer-123")
);

await transaction.CommitAsync();
```

## Message model

Both `OutboxMessage` and `InboxMessage` contain:

| Property | Description |
|----------|-------------|
| `EventId` | Unique event identifier. A unique index prevents duplicates for the same event id. |
| `CreatedAt` | When the message was written. |
| `Type` | CLR type name of the serialized payload. |
| `PartitionKey` | Logical partition used for concurrency and ordering. Defaults to `"default"`. |
| `Data` | Serialized message payload. |
| `RetryCount` | Number of failed processing attempts. |
| `ProcessedAt` | Null until the message is completed successfully. |

Handlers also receive `MessageMetadata` with `EventId`, `PartitionKey`, and `RetryCount`.

```csharp
using Underground.Outbox;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync( ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        var eventId = metadata.EventId;
        var partitionKey = metadata.PartitionKey;
        var retryCount = metadata.RetryCount;

        // Process the message
        return Task.CompletedTask;
    }
}
```

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

## Partitions

Partitions are central to how concurrency works.

- Each message has a `PartitionKey`.
- The processor first queries the distinct partitions that still have unprocessed messages.
- Different partitions can be handled in parallel up to `ParallelProcessingOfPartitions`.
- Within one partition, messages are fetched ordered by `id` and processed in batches.

Use the partition key to group messages that must stay ordered relative to each other, for example per aggregate, account, or customer.

If you do not care about partition-local ordering, the default partition key is `"default"`, but that means all messages compete in the same partition and you lose most of the parallelism.

## Multiple servers and duplicate prevention

The outbox can run on multiple servers against the same database.

It does not use a single global distributed lock. Instead, when a worker fetches messages for a partition it uses PostgreSQL `FOR UPDATE NOWAIT` row locking:

- one worker locks the next batch of rows for that partition
- another worker or server trying to fetch the same partition gets a lock failure
- that lock failure is treated as "someone else is already processing this partition"
- the second worker skips that partition and moves on

This is the main duplicate-prevention mechanism. Combined with marking successful messages via `ProcessedAt`, it prevents concurrent processors from handling the same rows twice.

## Error handling

Messages are processed inside the processor transaction, with a savepoint created for each message. When a handler fails, changes made while handling that message are rolled back to the savepoint, the message `RetryCount` is incremented, and processing of the current batch stops. Messages that were handled successfully earlier in the same batch remain processed.

You can configure exception policies per handler registration. To discard a message for a specific exception type, chain `OnException<TException>().Discard()` from `AddHandler`:

```csharp
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();

    cfg.AddHandler<ExampleMessageHandler, SecondMessage>()
        .OnException<InvalidOperationException>()
        .Discard();
});
```

`Discard()` deletes the failed message from the outbox or inbox table instead of leaving it available for retry. Exception policies are scoped to the specific handler and message type registration they are added to, so a handler that processes multiple message types can use different policies for each message type.

If no matching exception policy exists, the failed message stays in the table with an incremented `RetryCount`.

## Cleanup and retention

Processed inbox and outbox messages are not kept forever.

- `ProcessedMessageRetention` controls how long successfully processed messages are retained.
- `CleanupDelaySeconds` controls how often the cleanup hosted service runs.

Cleanup deletes rows where `ProcessedAt` is older than the configured retention cutoff.

## Configuration reference

| Setting | Default | Description |
|---------|---------|-------------|
| `BatchSize` | `5` | Number of messages processed in one transaction per partition batch. |
| `ParallelProcessingOfPartitions` | `4` | Number of partitions that can be processed concurrently. |
| `ProcessingDelayMilliseconds` | `4000` | Delay between scheduled processing cycles. |
| `ProcessedMessageRetention` | `7 days` | How long processed rows are kept before cleanup. |
| `CleanupDelaySeconds` | `3600` | Delay between cleanup runs. |

If you want one transaction per message, set `BatchSize = 1`.

## Example

```bash
dotnet run --project example/ConsoleApp/
```

## License

This project is licensed under the MIT License.
