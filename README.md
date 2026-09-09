# Outbox Library

`Underground.Outbox` is a .NET library for the transactional outbox and inbox patterns on top of EF Core and PostgreSQL.

It stores messages in the same database transaction as your business changes, then processes them in the background. The library is group-aware, can run on multiple application instances, and supports push-triggered processing through `IOutbox<TContext>.ProcessMessages()` — in-process only, so a commit on one instance does not wake another, which picks the work up on its next poll.

An application may hold **several inboxes and several outboxes**, one pair per `DbContext`, each in that context's own schema. This is what makes the library usable from a modular monolith: a message a module writes is handled by that module's handler against that module's tables. There is no transport here — the library carries a message from a module to itself, never between modules. See [Several outboxes in one application](#several-outboxes-in-one-application).

## How it works

Every message belongs to a **group**, identified by its `GroupKey`. A group offers only its **head message** — its oldest **stable** message that has not yet been completed, where stable means no still-running transaction could yet insert an earlier one into that group (see [Ordering](#ordering)). A group whose head message is not yet visible — because it is scheduled for later, because it is backing off after a failure, or because another worker currently holds it — offers nothing at all, rather than offering the message behind it.

Each worker runs one query that considers every group's head message at once and claims the oldest of them it can lock with `FOR UPDATE ... SKIP LOCKED`. A head message another worker already holds is skipped rather than waited for, so the claim falls through to the next group's head message. The worker then handles that one message and claims again.

Nothing hands groups out to workers; the database distributes them. Messages of one group are therefore handled one at a time, in order, and different groups proceed concurrently.

One claim is one message. There is no batch and no batch size on either side — throughput comes from how many groups your application defines, not from how many messages fit in a fetch. See [ADR 0003](docs/adr/0003-no-batching.md).

The two sides differ in where the transaction boundary sits, because their handlers do genuinely different things:

- An **inbox** handler applies an externally-originated event to this same database. One transaction spans the claim, the handler, and the write that records the message as handled.
- An **outbox** handler causes an effect *outside* this database — an HTTP call, a Kafka publish — whose latency we do not control, and holding a Postgres transaction open across that is what this design exists to avoid. The worker takes a time-bounded **lease** in a short transaction, commits it, dispatches with nothing open, and records the outcome in a second short transaction.

See [ADR 0001](docs/adr/0001-split-transaction-model-between-inbox-and-outbox.md).

## Delivery guarantees

**Outbox delivery is at-least-once. Outbox handlers must be idempotent.**

A lease is time-bounded, which is what stops an instance that dies mid-delivery from blocking its group forever: the lease expires on its own and the message is offered again. The price is that a worker which died — or merely overran — after the external effect but before the completion write causes that effect to happen twice. A worker that finishes after its lease expired detects the loss, logs a warning, and discards its outcome, so the message is not fanned out any further.

**Inbox delivery is exactly-once.** The handler's writes and the record that the message was handled commit in the same transaction, so either both happen or neither does. That is a guarantee about the *effect on the database*: under a retrying execution strategy the handler may be run more than once, so keep its effects inside the transaction. See [Connection retries and execution strategies](#connection-retries-and-execution-strategies).

## Ordering

Messages within a group are handled strictly in order — but that order is **the order in which the writing transactions started**, not the order in which the rows were appended.

An identity value is assigned when a row is inserted, not when its transaction commits, so ordering by `id` alone lets a transaction that started later but committed first have its message handled first. Every message therefore carries a `TransactionId`, messages are ordered by `(TransactionId, Id)`, and a message is offered only once it is **stable** — once no still-running transaction could yet insert an earlier message into its group. See [ADR 0002](docs/adr/0002-order-by-transaction-id-not-sequence.md).

Two consequences are worth knowing before you rely on this:

- **A long-running write transaction anywhere in the database delays delivery.** The stability test is against the snapshot minimum, which an open write transaction holds back, so a long writer stalls *all* message delivery until it commits — not just delivery of its own group. Read-only transactions are unaffected, as they are assigned no transaction id. This is the same coupling logical replication and CDC have, and it needs monitoring.
- **A slow inbox handler does the same thing.** An inbox handler runs inside the transaction that claims its message and records the outcome, and that transaction is a writer for its whole length — so while it runs it holds the snapshot minimum back exactly as any other long writer would, delaying delivery for every group on both the inbox and the outbox. This is the price of exactly-once inbox delivery ([ADR 0001](docs/adr/0001-split-transaction-model-between-inbox-and-outbox.md)); keep inbox handlers short, and move slow or external work to the outbox, whose claim transaction commits immediately.
- Messages appended within one transaction keep their relative order.

## Two behaviours that look like defects

Both are deliberate, and knowing about them up front is cheaper than discovering them in production.

**A permanently failing message stalls its own group, forever.** It is retried with an exponential backoff up to a ten-minute ceiling and never given up on, and every message behind it in that group waits. Other groups are unaffected. Under strict per-group ordering the alternative — skipping past it — silently drops a message out of an ordered stream, which is worse for a consumer that depends on the order. Detection is external: a group that stops draining shows up as unbounded growth in unhandled messages. See [ADR 0004](docs/adr/0004-poison-messages-block-their-group.md). Until a dead-letter mechanism exists, `Discard()` exception policies are the way to drop a known-bad message.

**A scheduled or backing-off message holds back everything behind it in its group.** A group offers only its head message, so nothing written after a message scheduled for tomorrow is handled until that message has been. Give a message its own group key if the delay is meant to apply to it alone.

## Features

- **EF Core based**: built on top of EF Core abstractions and DbContexts.
- **Outbox and inbox support**: both sides share the claim model and the per-message chain, and diverge only where their transaction models genuinely differ.
- **Push-triggered processing**: you can call `IOutbox<TContext>.ProcessMessages()` to schedule a run immediately after commit. You can use a dbcontext interceptor to automate this.
- **One inbox and outbox per `DbContext`**: each with its own schema, handlers, timeouts, retention and concurrency, and none of them able to reach another's rows.
- **Background processing**: hosted services also schedule processing runs on a configurable delay.
- **Group-aware parallelism**: different groups are handled concurrently.
- **Multi-instance safe**: multiple servers can process the same table without duplicating work under normal operation.
- **Total ordering within a group**: ordering holds even when two of your own transactions write to the same group concurrently.
- **Scheduled delivery**: a message can be given the instant from which it may be handled.
- **Retry backoff**: failed messages are retried with an exponential, jittered delay computed by the database.
- **Bounded handler runtime**: every handler is cancelled once `HandlerTimeout` elapses, so a hung external call cannot occupy a worker for good.
- **Retention cleanup**: completed messages are deleted automatically after a configurable retention period.
- **Distributed tracing**: each message is handled inside an OpenTelemetry `process` span that continues the trace of the transaction that wrote it.
- **Source generation**: avoids runtime reflection for handler dispatch and DI wiring.

## Requirements

- .NET / EF Core application
- **PostgreSQL 13 or newer**, via `Npgsql`

PostgreSQL 13 is the floor because ordering depends on the 64-bit transaction identifier type `xid8` and on `pg_current_xact_id()`, `pg_current_snapshot()` and `pg_snapshot_xmin()`, all of which arrived in that release. Claiming also relies on `FOR UPDATE ... SKIP LOCKED`.

### Table names and the schema

The library stores its messages in two tables named `inbox` and `outbox`. The names are fixed: mapping either entity to a different table or column through EF Core is not supported, and doing so breaks the library at its first claim rather than at startup.

The **schema** is yours to choose, and you state it at registration:

```csharp
builder.Services.AddAppDbContextOutboxServices(cfg =>
{
    cfg.Schema = "public";
    // ...
});
```

Every claim, completion, retry and cleanup statement qualifies its table with that schema — `"orders".outbox` — so `search_path` is not load-bearing and two contexts sharing a connection string reach two different table pairs. It is required and nothing infers it: a registration without a `Schema` throws, and a *wrong* one fails at the first claim with PostgreSQL's `42P01`.

The schema is said twice, and the two must agree: once in `OnModelCreating` (or by leaving the default), which is where EF creates the tables and where it inserts, deletes and sweeps; and once at registration, which is where the raw claim, completion and retry statements look. Nothing reconciles them — registration has no model to read ([ADR 0007](docs/adr/0007-schema-is-chosen-at-registration.md)) — so a `DbContext` that registers `"orders"` while its model still maps to `public` writes to one table and claims from another, and its messages are never handled. The claim fails loudly with `42P01` in the worker's error log; the write does not.

Registering two `DbContext`s against the same schema throws immediately — that is the one way two modules could silently share a table pair. It compares the strings you gave it, so a module that gives a distinct schema here and forgets `HasDefaultSchema` is not caught by that check.

See [ADR 0005](docs/adr/0005-fixed-table-and-column-names.md) for why the names are fixed rather than read off the EF model, and [ADR 0007](docs/adr/0007-schema-is-chosen-at-registration.md) for why the schema is stated rather than inferred.

## Getting started

### Installation

```bash
dotnet add package Underground.Outbox
dotnet add package Underground.Outbox.SourceGenerator
```

**Important**: The source generator package must be added to the root/main project where dependency injection is configured. Other referenced projects only need to import the main `Underground.Outbox` package.

### Configuration

1. **Adjust DbContext**: Add interfaces and message types to your DbContext. This ensures that you can use EF migrations to add the tables to your database. The context must be `public`, because the generated registration method names it.

    ```csharp
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IOutboxDbContext, IInboxDbContext
    {
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<InboxMessage> InboxMessages { get; set; }
    }
    ```

2. **Handle Messages**: Implement `IOutboxMessageHandler<T>` or `IInboxMessageHandler<T>`, and name the `DbContext` the handler belongs to:

    ```csharp
    [OutboxHandler<AppDbContext>]
    public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
    {
        public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
    ```

    The attribute is required. A handler without one is a build error (`OUTBOX002`), because no dispatcher would ever call it.

3. **Add Services**: The source generator emits one registration method per `DbContext` it found handlers for, named after that context — `AddAppDbContextOutboxServices` for an `AppDbContext`:

    ```csharp
    builder.Services.AddAppDbContextOutboxServices(cfg =>
    {
        cfg.Schema = "public";
        cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();
        cfg.AddHandler<ExampleMessageHandler, AnotherMessage>();
    });

    builder.Services.AddAppDbContextInboxServices(cfg =>
    {
        cfg.Schema = "public";
        cfg.AddHandler<InboxMessageHandler, ExampleMessage>();
    });
    ```

    Register an outbox without an inbox, or an inbox without an outbox, as your module needs.

### Add messages

Adding to the outbox requires an active database transaction. That is intentional: the outbox write must commit together with your business data.

`ExecuteInTransactionAsync` opens that transaction for you, through whichever execution strategy the host configured, so the same call works with and without connection retries:

```csharp
using Underground.Outbox.Data;

await dbContext.ExecuteInTransactionAsync(async ct =>
{
    order.Status = OrderStatus.Shipped;
    await dbContext.SaveChangesAsync(ct);

    await outbox.AddMessageAsync(
        dbContext,
        new OutboxMessage(
            Guid.NewGuid(),
            DateTime.UtcNow,
            new ExampleMessage("Hello, World!"),
            groupKey: "customer-123"),
        ct);
}, cancellationToken);
```

There is a `Func<CancellationToken, Task<T>>` overload when the work returns something. A transaction that is already open is joined rather than nested, so a method using this stays callable from inside another one.

Owning the transaction yourself works too, as long as no retrying execution strategy is configured:

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

Use `AddMessageAsync` / `AddMessagesAsync` rather than tracking the entity yourself. Adding an `OutboxMessage` straight to the `DbSet` still works and is still processed, but it skips the trace-context capture below, so the message is handled in a trace of its own.

To schedule a message instead of delivering it as soon as possible, pass `visibleAt`:

```csharp
new OutboxMessage(
    Guid.NewGuid(),
    DateTime.UtcNow,
    new ReminderMessage("Your trial ends today"),
    groupKey: "customer-123",
    visibleAt: DateTime.UtcNow.AddDays(1));
```

### Connection retries and execution strategies

Aspire's `EnrichNpgsqlDbContext`, and a plain `EnableRetryOnFailure()`, configure a **retrying execution strategy**. EF then refuses to run a query or `SaveChanges` inside a transaction you began yourself — which is every way of staging an outbox message:

> The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions.

`ExecuteInTransactionAsync` above avoids this entirely. If you own the transaction yourself, run it through the strategy:

```csharp
var strategy = dbContext.Database.CreateExecutionStrategy();

await strategy.ExecuteAsync(async () =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

    await outbox.AddMessageAsync(dbContext, message, cancellationToken);

    await transaction.CommitAsync(cancellationToken);
});
```

Either way the delegate must be **re-runnable**: a transient failure replays the whole of it, so stage what it needs inside it rather than before the call.

The library's own processing adapts to the same strategy, and one consequence reaches your code. The inbox runs claim, handler and outcome write in a single transaction ([ADR 0001](docs/adr/0001-split-transaction-model-between-inbox-and-outbox.md)), so that transaction is also its retry unit: **an inbox handler may be run more than once**, even though its effect on the database still lands exactly once — either the attempt rolled back entirely, or it committed and the replay finds the message no longer offered. Keep an inbox handler's effects inside its transaction and this costs nothing; give it an effect the transaction cannot roll back and a replay will repeat that effect. Outbox handlers are unaffected: only the claim is replayed, never the dispatch. See [ADR 0006](docs/adr/0006-adapt-to-the-host-execution-strategy.md).

## Message model

Both `OutboxMessage` and `InboxMessage` contain:

| Property | Description |
|----------|-------------|
| `EventId` | Unique event identifier. A unique index prevents duplicates for the same event id. |
| `TransactionId` | The identifier of the transaction that inserted the message, assigned by the database. Together with `Id` it is the sort key that makes ordering within a group total. |
| `CreatedAt` | When the message was written. |
| `Type` | Runtime CLR type name of the serialized payload — `Type.FullName`. See [The `Type` column is a contract](#the-type-column-is-a-contract). |
| `GroupKey` | Logical group used for concurrency and ordering. Defaults to `"default"`. |
| `Data` | Serialized message payload. |
| `RetryCount` | Number of failed processing attempts. |
| `VisibleAt` | The instant from which the message may be handled. Defaulted by the database to the present, pushed into the future by the retry backoff, and — on the outbox — set to the lease expiry while a worker holds the message. |
| `CompletedAt` | Null until the message is completed successfully. |
| `TraceParent` | The W3C trace context of the transaction that wrote the message, so handling it continues the same trace. Null when nothing was tracing. |

Both types are mapped to fixed tables and columns — `inbox` and `outbox` — which cannot be remapped; see [Table names and the schema](#table-names-and-the-schema).

### The `Type` column is a contract

`Type` is written from the payload's runtime type (`data.GetType().FullName`) and read back by the
generated dispatcher, which selects a handler by comparing it against `typeof(T).FullName` for each
handler it found. Both sides evaluate the same expression, so nested types (`Outer+Inner`) and generic
types (`` Wrapped`1[[...]] ``) match as they should.

That name is persisted, and rows outlive the code that wrote them. Two consequences:

- **Renaming a message class, or moving it to another namespace, orphans the rows already in the table.**
  Nothing fails at build time; the messages simply find no handler and, per
  [ADR 0004](docs/adr/0004-poison-messages-block-their-group.md), block their group. Drain the table
  before such a rename, or keep the old type around with a handler until it has drained.
- **A generic message type's name embeds the assembly version of its type arguments**, because that is
  what `Type.FullName` produces. Bumping the assembly version orphans rows the same way. Prefer a
  non-generic message type unless the table is always drained across deployments.

On the inbox the same name is the integration contract: a foreign producer has to write your .NET type
name into the `type` column for the message to be dispatched.

Two handlers of the same kind bound to the **same** `DbContext` for one message type is a build error
(`OUTBOX001`) — only one of them could ever run. Two modules handling the same message type is legal:
handler registrations are keyed on the context, so each module's dispatcher can only reach its own.

Handlers also receive `MessageMetadata` with `EventId`, `GroupKey`, and `RetryCount`.

```csharp
using Underground.Outbox;
using Underground.Outbox.Attributes;

[OutboxHandler<AppDbContext>]
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

This library supports push-based processing through `IOutbox<TContext>.ProcessMessages()`.

That means the producer side can add messages, commit the transaction, and then trigger processing right away instead of waiting for the next scheduled cycle.

If you want this to happen automatically after `transaction.CommitAsync()`, register the built-in EF Core interceptor:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options
        .UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<ProcessMessagesOnSaveChangesInterceptor<AppDbContext>>());
});
```

With that registration in place, a successful `transaction.CommitAsync()` call will wake the workers of **that context's** inbox and outbox when new `OutboxMessage` or `InboxMessage` rows were inserted in that unit of work. A commit in one module does not wake another module's workers; each context gets its own interceptor.

## Tracing

Handling a message emits one OpenTelemetry span per attempt. Subscribe to it by name:

```csharp
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing
    .AddSource(OutboxTelemetry.ActivitySourceName)   // "Underground.Outbox"
    .AddNpgsql());                                   // the statements underneath, if you want them
```

Nothing is emitted until something subscribes: the library uses `System.Diagnostics.ActivitySource` and takes no dependency on the OpenTelemetry packages.

`AddMessageAsync` records the ambient trace context on the message's `traceparent` column, and the worker that later handles it starts its span as a child of that context — so the request that caused the message and the handler that carries out its effect appear in one trace, however long the message waited. A message written while nothing was tracing has no context and is handled under a trace of its own. See [ADR 0006](docs/adr/0006-message-trace-context-is-stored-and-parented.md) for why the span parents to that context rather than linking to it.

The span is named `process outbox` / `process inbox`, is a `Consumer` span, and carries:

| Attribute | Value |
|-----------|-------|
| `messaging.system` | `underground_outbox` |
| `messaging.operation.name`, `messaging.operation.type` | `process` |
| `messaging.destination.name` | `outbox` or `inbox` |
| `messaging.destination.partition.id` | The message's `GroupKey` |
| `messaging.message.id` | The message's `EventId` |
| `underground.outbox.context` | The `DbContext` this inbox or outbox belongs to |
| `underground.outbox.message.type` | The message's `Type` |
| `underground.outbox.retry_count` | The attempt number |
| `error.type` | Only on failure — see below |

A successful attempt leaves the span's status unset. A failed one sets `Error` and records the exception, with `error.type` naming the exception type. Two failures are the library's own rather than a handler's:

- `lease_lost` — the handler threw, and by the time the failure was written the lease had expired, so nothing was recorded and another worker already has the message.
- `duplicate_delivery` — the handler *succeeded* and the completion write came too late. The effect has been carried out and will be carried out again. This is the one span worth alerting on.

Shutdown mid-attempt is not an error: the span closes with its status unset.

### Migration

`traceparent` is a new nullable column on both tables, so upgrading needs an EF migration:

```bash
dotnet ef migrations add AddTraceParentToInboxAndOutbox
dotnet ef database update
```

Existing rows keep a null and are handled exactly as before.

## Several outboxes in one application

An inbox and an outbox are bound to a `DbContext`. A modular monolith registers one pair per module, each in that module's schema, and nothing any module owns is reachable from another.

```csharp
builder.Services.AddOrdersContextOutboxServices(cfg =>
{
    cfg.Schema = "orders";
    cfg.AddHandler<ShipOrderHandler, OrderShipped>();
});

builder.Services.AddBillingContextOutboxServices(cfg =>
{
    cfg.Schema = "billing";
    cfg.MaxConcurrentGroups = 16;          // this module's setting, not everyone's
    cfg.AddHandler<InvoiceHandler, OrderShipped>();
});
```

Three things make that hold:

- **The `DbContext` is the key.** You resolve `IOutbox<OrdersContext>`, not `IOutbox`, so the compiler says which module's outbox you are writing to. Each module's processing uses its own context, and so its own execution strategy.
- **The schema is stated at registration.** Two modules sharing a connection string reach two different table pairs, and registering both against one schema throws. State the same schema on the module's `DbContext` too — see [Table names and the schema](#table-names-and-the-schema).
- **A handler names its `DbContext`.** The generator emits one dispatcher per context, and handler registrations are keyed on the context type — so two modules may declare handlers for the same message type, and neither can be given the other's message.

One hosted service drives every registered inbox and outbox, and one cleanup loop covers all of them, so adding a module costs workers rather than background loops.

### What this is not

**There is no transport.** The library carries a message from a module to itself: written in one transaction, handled by that module's worker. A module that wants to reach a neighbour calls it by whatever in-process means your application already has, and the neighbour writes to its own inbox in its own transaction. Adding several outboxes does not add a relay between them.

**Wrapping a neighbour's contract type is optional.** Handlers no longer need distinct message types to stay apart, so wrapping is now a durability choice rather than a requirement. The `type` column stores the handled type's `FullName`, so an unwrapped message persists a *neighbour's* type name; when they rename or move it, rows already written stop matching any branch and — per [ADR 0004](docs/adr/0004-poison-messages-block-their-group.md) — block their group indefinitely. Wrapping makes the persisted name yours. Nothing enforces either choice.

### What it costs

**A slow inbox handler in one module stalls head-message discovery in every other.** An inbox handler holds a write transaction for its whole duration, and the stability gate is instance-wide ([ADR 0002](docs/adr/0002-order-by-transaction-id-not-sequence.md)), so this is not a per-module cost. Keep inbox handlers short; an inbox handler's job is to write its own module's tables and return.

**Workers multiply, and they share a connection pool.** The arithmetic is `outboxes × 2 × MaxConcurrentGroups` worker loops — the ×2 being the claim connection and the handler's own — plus one cleanup scope, all against whatever pool the shared connection string configures. Size the pool for that total, not for one module's.

See [ADR 0008](docs/adr/0008-outboxes-are-bound-to-a-dbcontext.md).

## Upgrading from a single outbox

This is one breaking major version. An existing single-outbox registration translates mechanically:

| Before | After |
|--------|-------|
| `AddOutboxServices<AppDbContext>(cfg => …)` | `AddAppDbContextOutboxServices(cfg => …)` — generated per context, named after it |
| `AddInboxServices<AppDbContext>(cfg => …)` | `AddAppDbContextInboxServices(cfg => …)` |
| *(nothing)* | `cfg.Schema = "public";` — required; `"public"` reproduces the default arrangement |
| `search_path` on the connection string | `cfg.Schema = "app";`, and drop the `Search Path=` setting |
| `IOutbox` / `IInbox` | `IOutbox<AppDbContext>` / `IInbox<AppDbContext>` |
| `ProcessMessagesOnSaveChangesInterceptor` | `ProcessMessagesOnSaveChangesInterceptor<AppDbContext>` |
| `public class H : IOutboxMessageHandler<M>` | `[OutboxHandler<AppDbContext>] public class H : IOutboxMessageHandler<M>` — required (`OUTBOX002`) |
| `IMessageExceptionHandler<T>.HandleAsync(…, IDbContext, …)` | `…HandleAsync(…, DbContext, …)`; `IDbContext` is gone |
| an internal `DbContext` | a `public` one, since the generated registration method names it |

The handler interfaces themselves are unchanged, so adopting this costs an attribute rather than a rewrite. There is no compatibility shim for the non-generic `IOutbox` / `IInbox`.

## Choosing group keys

Groups are the unit of both ordering and concurrency, and the only source of parallelism *within one inbox or outbox*. Group keys are **not comparable across them**: two modules using the same group key are not serialised against each other, because a group has no meaning outside the table it lives in.

Use the group key to group messages that must stay ordered relative to each other, for example per aggregate, account, or customer. Messages that have no ordering relationship belong in different groups.

The default group key is `"default"`. Leaving it there puts every message in one group, which means strictly serial handling and no parallelism at all — and it means one permanently failing message stalls everything.

`MaxConcurrentGroups` is the number of workers, and with it the ceiling on how many groups are in flight at once. `1` means strictly serial handling across all groups — one message anywhere in the system at a time — rather than one message per group.

## Multiple servers

The library can run on multiple servers against the same database, with no global distributed lock and no coordination between instances.

The claim query is what keeps two workers apart: one worker locks the head message it claims, another worker or server running the same query finds that row locked and skips it rather than waiting, and so ends up on a different group.

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
builder.Services.AddAppDbContextOutboxServices(cfg =>
{
    cfg.Schema = "public";

    cfg.AddHandler<ExampleMessageHandler, ExampleMessage>();

    cfg.AddHandler<ExampleMessageHandler, SecondMessage>()
        .OnException<InvalidOperationException>()
        .Discard();

    cfg.Policies.OnException<DataException>()
        .Discard();
});
```

`Discard()` deletes the failed message from the outbox or inbox table instead of leaving it available for retry. Exception policies can be scoped to a specific handler and message type registration, or configured globally through `cfg.Policies`.

Exactly one policy runs. If any registration-specific policy matches, the global policies are not consulted at all — however broad the registration's exception type and however narrow the global one — so a single `AddHandler` chain reads as a complete override. Among the policies at one level the nearest matching exception type wins, as in a `catch` block; the same exception type registered twice at one level keeps the first registration. Policies are terminal and mutually exclusive by design: a kind that composes with another, such as retrying before dead-lettering, needs a deliberate change to this selection rule rather than a second registration.

If no matching exception policy exists, the failed message stays in the table with an incremented `RetryCount` and is retried once its backoff has elapsed — forever, stalling its group, per [ADR 0004](docs/adr/0004-poison-messages-block-their-group.md).

## Cleanup and retention

Completed inbox and outbox messages are not kept forever.

- `CompletedMessageRetention` controls how long completed messages are retained.
- `CleanupDelaySeconds` controls how often the cleanup hosted service runs.

Cleanup deletes rows where `CompletedAt` is older than the configured retention cutoff.

**On the inbox, `CompletedMessageRetention` is your duplicate-suppression window.** A redelivered event is rejected because its `EventId` already exists in the inbox table — and that only works while the row is still there. Once cleanup has deleted it, the same event is accepted again and handled a second time. Set the retention to at least the longest window over which the systems that send you events might redeliver one; shortening it does not fail loudly, it silently starts accepting duplicates.

The same setting on the outbox is only about table size: nothing else reads a completed outbox row.

## Configuration reference

Every setting below is per inbox and per outbox: a module with a slow external partner does not impose its timeouts on a module without one, and there is no concurrency budget shared across outboxes.

| Setting | Default | Description |
|---------|---------|-------------|
| `Schema` | *(required)* | The PostgreSQL schema this side's table lives in. Registration throws when it is unset, and two `DbContext`s naming one schema is refused. |
| `MaxConcurrentGroups` | `4` | Number of workers, and with it the number of groups that can be handled concurrently. `1` means strictly serial handling across all groups. |
| `HandlerTimeout` | `45 seconds` | Time a handler is given before its cancellation token fires and the attempt is recorded as failed. The outbox lease is derived from this plus a margin for the completion write, and is deliberately not configurable on its own: a lease shorter than the timeout would guarantee double delivery on every slow message. |
| `BackoffBase` | `1 second` | Delay before a message that failed for the first time is offered again. |
| `MaxBackoff` | `10 minutes` | Ceiling the doubling retry delay stops at. Jitter is applied afterwards, so an actual delay may exceed this by the jitter proportion. |
| `BackoffJitter` | `0.2` | Proportion each retry delay is randomly varied by, either way. `0` gives exact delays. |
| `ProcessingDelayMilliseconds` | `4000` | Delay between scheduled processing cycles. |
| `CompletedMessageRetention` | `7 days` | How long completed rows are kept before cleanup. On the inbox this is also the duplicate-suppression window. |
| `CleanupDelaySeconds` | `3600` | Delay between cleanup runs. |

## Example

```bash
dotnet run --project example/ConsoleApp/
```

## License

This project is licensed under the MIT License.
