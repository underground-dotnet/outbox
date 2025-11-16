# Outbox Library

The Outbox Library is a .NET Core library designed to simplify the implementation of the outbox pattern in your applications. The outbox pattern ensures reliable message delivery by storing messages in a database and processing them asynchronously, making it ideal for distributed systems and event-driven architectures.

## Features

- **EF Core**: This library is fully built on top of EF Core.
- **Inbox and Outbox Support**: Provides interfaces and implementations for managing inbox and outbox messages.
- **Transactional**: Message batches are processed within one transaction.
- **Error Handling**: Built-in exception handling for common scenarios.

## Getting Started

### Installation

To use the Underground Outbox Library in your project, add the NuGet package:

```bash
dotnet add package Underground.Outbox
```

### Configuration

1. **Add Services**: Configure the outbox services in your `Program.cs` file:

    ```csharp
    builder.Services.AddOutboxServices<AppDbContext>(cfg =>
    {
        cfg.AddHandler<ExampleMessageHandler>();
    });

    builder.Services.AddInboxServices<AppDbContext>(cfg =>
    {
        cfg.AddHandler<InboxMessageHandler>();
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

    ```csharp
    using Underground.Outbox;

    public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
    {
        public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
        {
            // Process the message
            return Task.CompletedTask;
        }
    }
    ```

### Usage

1. **Add Messages to the Outbox**:

    ```csharp
    using var transaction = await dbContext.Database.BeginTransactionAsync();
    await outbox.AddMessageAsync(new ExampleMessage { Content = "Hello, World!" });
    await transaction.CommitAsync();
    ```
2. **The background processor will call the handlers during the next run.**

## Example

Run example:

```bash
dotnet run --project example/ConsoleApp/
```

## License

This project is licensed under the MIT License.
