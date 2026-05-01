# Outbox Library

The Outbox Library is a .NET Core library designed to simplify the implementation of the outbox pattern in your applications. The outbox pattern ensures reliable message delivery by storing messages in a database and processing them asynchronously, making it ideal for distributed systems and event-driven architectures.

## Transactional outbox

There are usually two different types of implementations when it comes to the outbox pattern:

### Using multiple transactions

The outbox processor is fetching a batch of outbox messages and marks them as `in processing` by updating a column in the outbox table. Then using a second transaction to process the messages.

Using this approach you have short lived transactions, but you need to deal with cases of releasing the lock on the rows when a processor fails.

### Using one transaction

The outbox processor is fetching a batch of outbox messages (sometimes with the addition of `SELECT FOR UPDATE SKIP LOCKED`) and uses the same transaction to process the messages.

This ensure that updating the messages table as well as any business logic tables is done in the same transaction (all or nothing). For larger batches it increases the risk of long running transactions.

For this library the single transaction approach was chosen. Messages in a batch will be processed until processing of one message fails.

## Features

- **EF Core**: This library is fully built on top of EF Core.
- **Inbox and Outbox Support**: Provides interfaces and implementations for managing inbox and outbox messages.
- **Transactional**: Message batches are processed within one transaction.
- **Distributed Lock**: When multiple instances of the application are running then a distributed lock ensures that the outbox is only processed by a single conusmer.
- **Error Handling**: Built-in exception handling for common scenarios.
- **Source Generation**: Uses C# source generators to eliminate reflection overhead and improve performance.

## Getting Started

### Installation

To use the Underground Outbox Library in your project, add the NuGet packages:

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

### Message Metadata

The handlers receive a `MessageMetadata` parameter containing:

| Property | Type | Description |
|----------|------|-------------|
| `EventId` | `Guid` | Unique identifier for the message |
| `PartitionKey` | `string` | Partition key used for message routing |
| `RetryCount` | `int` | Number of times this message has been retried (0 initially) |

```csharp
using Underground.Outbox;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        var eventId = metadata.EventId;
        var partitionKey = metadata.PartitionKey;
        var retryCount = metadata.RetryCount;

        // Process the message
        return Task.CompletedTask;
    }
}
```

### Usage

1. **Add Messages to the Outbox**:

    ```csharp
    using var transaction = await dbContext.Database.BeginTransactionAsync();
    await outbox.AddMessageAsync(dbContext, new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage { Content = "Hello, World!" }));
    await transaction.CommitAsync();
    ```
2. **The background processor will call the handlers during the next run.**

### Error Handling

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

## Example

Run example:

```bash
dotnet run --project example/ConsoleApp/
```

## License

This project is licensed under the MIT License.
