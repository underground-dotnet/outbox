# Underground.Outbox

A .NET library for the transactional **outbox** and **inbox** patterns on EF Core and PostgreSQL.

Messages are written in the same database transaction as your business changes and handled in the
background, in order per group, safely across multiple application instances.

- **Outbox**: deliver an effect *outside* the database (HTTP call, Kafka publish) after your change commits. At-least-once.
- **Inbox**: apply an incoming event to *this* database. Exactly-once, with duplicate suppression by `EventId`.

## Requirements

- .NET 10 with EF Core, built with **.NET SDK 10.0.400 or newer**
- **PostgreSQL 13 or newer**, via Npgsql

## Getting started

### 1. Install

```bash
dotnet add package Underground.Outbox
dotnet add package Underground.Outbox.SourceGenerator
```

Add the source generator to **every project that declares handlers**. Handlers in a project without it
are not registered.

### 2. Add the tables to your DbContext

```csharp
sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IOutboxDbContext, IInboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }
}
```

Then create the `outbox` and `inbox` tables with an EF migration (`dotnet ef migrations add AddOutbox`).
Implement only the interface for the side you use.

### 3. Write a handler

```csharp
public class OrderShippedHandler : IOutboxMessageHandler<OrderShipped>
{
    public async Task HandleAsync(OrderShipped message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        // metadata.EventId, metadata.GroupKey, metadata.RetryCount
        await httpClient.PostAsJsonAsync("/shipments", message, cancellationToken);
    }
}
```

Inbox handlers implement `IInboxMessageHandler<T>` the same way. Handlers are discovered automatically
and registered as `Transient`; change that with `[MessageHandlerLifetime(ServiceLifetime.Scoped)]`.
Each message type can have only one outbox and one inbox handler.

### 4. Register the services

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseNpgsql(connectionString)
    // optional: start processing right after commit instead of on the next poll
    .AddInterceptors(sp.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor>()));

builder.Services.AddOutboxServices<AppDbContext>(cfg => { });
builder.Services.AddInboxServices<AppDbContext>(cfg => { });

// generated per project that declares handlers, named after its assembly
builder.Services.AddMyAppMessageHandlers();
builder.Services.AddMyOrdersModuleMessageHandlers();
```

### 5. Add messages

Adding a message requires a transaction, so it commits together with your business data.
`ExecuteInTransactionAsync` opens one (or joins an existing one) and works with retrying execution
strategies:

```csharp
using Underground.Outbox.Data;

await dbContext.ExecuteInTransactionAsync(async ct =>
{
    var order = await dbContext.Orders.SingleAsync(o => o.Id == orderId, ct);
    order.Status = OrderStatus.Shipped;
    await dbContext.SaveChangesAsync(ct);

    await outbox.AddMessageAsync(
        dbContext,
        new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new OrderShipped(order.Id), groupKey: $"order-{order.Id}"),
        ct);
}, cancellationToken);
```

The inbox works the same through `IInbox.AddMessageAsync` with an `InboxMessage`.

Useful variations:

- **Schedule** a message with `visibleAt: DateTime.UtcNow.AddDays(1)`.
- **Batch** with `AddMessagesAsync`.
- **Save once**: `StageMessage` / `StageMessages` only add the message to the change tracker, so your
  own `SaveChangesAsync` writes it. A staged message that is never saved is lost.
- **Own transaction**: `BeginTransactionAsync` / `CommitAsync` works too, unless a retrying execution
  strategy is configured (e.g. Aspire's `EnrichNpgsqlDbContext` or `EnableRetryOnFailure()`); then use
  `ExecuteInTransactionAsync`. See [ADR 0006](docs/adr/0006-adapt-to-the-host-execution-strategy.md).

The delegate passed to `ExecuteInTransactionAsync` may be replayed after a transient failure, so load
what it needs inside it.

## Groups and ordering

Every message has a `GroupKey` (default `"default"`). Groups are the unit of ordering and of concurrency:

- Messages in one group are handled one at a time, in the order their transactions started.
- Different groups are handled concurrently, up to `MaxConcurrentGroups`.

Pick a key per aggregate, account or customer: whatever must stay ordered. Leaving everything in
`"default"` means strictly serial processing, and one failing message blocks all others.

## Delivery guarantees

- **Outbox is at-least-once. Make outbox handlers idempotent.** A worker that dies, or a handler that times
  out, after the external effect but before recording success causes a second delivery.
- **Inbox is exactly-once for database effects.** The handler runs in the same transaction that marks
  the message handled. Under a retrying execution strategy the handler itself may run more than once,
  so keep its effects inside that transaction.
- Handlers receive a `CancellationToken` that fires after `HandlerTimeout`; honour it. An outbox handler
  that ignores it keeps its lease renewed, so its group waits until it returns
  ([ADR 0011](docs/adr/0011-renew-the-lease-of-a-handler-that-overruns.md)).

## Error handling

A handler that throws (or times out) is retried with exponential, jittered backoff up to `MaxBackoff`,
**forever**. Until it succeeds, the messages behind it in the same group wait; other groups are unaffected
([ADR 0004](docs/adr/0004-poison-messages-block-their-group.md)). Monitor for groups that stop draining.

To drop messages for known exceptions, configure a `Discard()` policy per handler or globally:

```csharp
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.ForHandler<OrderShippedHandler, OrderShipped>()
        .OnException<InvalidOperationException>()
        .Discard();

    cfg.Policies.OnException<DataException>()
        .Discard();
});
```

Exactly one policy runs: a matching handler-specific policy wins over any global one, and within a
level the most specific exception type wins, like a `catch` block.

## Things to know before production

- **Long write transactions delay all delivery.** Ordering waits until no running transaction could still
  insert an earlier message, so any long-running writer in the database (including a slow inbox handler)
  holds back every group. Keep inbox handlers short; move slow work to the outbox.
  ([ADR 0002](docs/adr/0002-order-by-transaction-id-not-sequence.md))
- **A scheduled or retrying message blocks its group.** Give a delayed message its own group key if the
  delay should apply to it alone.
- **The message type name is persisted.** Renaming or moving a message class orphans rows already in the
  table. Drain the table first, or keep the old type and its handler until it has drained. Avoid generic
  message types: their name includes assembly versions. On the inbox, external producers must write this
  .NET type name into the `type` column.
- **Inbox retention is your duplicate window.** Duplicates are rejected only while the original row
  exists, so set `CompletedMessageRetention` to at least the longest redelivery window of your senders.
- **Table names are fixed** (`outbox`, `inbox`) and queried unqualified. If they live in a non-default
  schema, set it on the connection: `Search Path=app`.
  ([ADR 0005](docs/adr/0005-fixed-table-and-column-names.md))
- **Push-triggered processing is in-process.** A commit on one instance wakes only that instance; others
  pick the work up on their next poll.

## Configuration

Set on `cfg` in `AddOutboxServices` / `AddInboxServices`:

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentGroups` | `2` | Number of workers, and so the number of groups handled at once. |
| `HandlerTimeout` | `45 seconds` | When a handler's cancellation token fires and the attempt counts as failed. |
| `BackoffBase` | `1 second` | Retry delay after the first failure; doubles on each further failure. |
| `MaxBackoff` | `10 minutes` | Ceiling for the retry delay (before jitter). |
| `BackoffJitter` | `0.2` | Random variation applied to each retry delay. `0` disables it. |
| `ProcessingDelayMilliseconds` | `10000` | Poll interval between processing cycles. |
| `CompletedMessageRetention` | `7 days` | How long completed messages are kept. Also the inbox duplicate window. At most 365 days. |
| `CleanupInterval` | `1 hour` | Interval between cleanup runs. 1 second to 31 days. |

## Tracing

Each handling attempt emits an OpenTelemetry `Consumer` span (`process outbox` / `process inbox`) that
continues the trace of the request that wrote the message:

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(OutboxTelemetry.ActivitySourceName));
```

The library uses `System.Diagnostics.ActivitySource` and has no OpenTelemetry dependency. A span with
`error.type = duplicate_delivery` means an outbox effect will be carried out twice; it is worth alerting on.
Messages added directly to the `DbSet` instead of through `AddMessageAsync` start a new trace.

## Upgrading

New versions may add columns or indexes to the `outbox` and `inbox` tables. After updating the package,
add and apply an EF migration:

```bash
dotnet ef migrations add UpdateOutbox
dotnet ef database update
```

## Further reading

- [`example/ConsoleApp`](example/ConsoleApp) — runnable sample (`dotnet run --project example/ConsoleApp/`, needs Docker)
- [`docs/adr`](docs/adr) — design decisions: transaction model, ordering, no batching, poison messages, claiming

## License

MIT
