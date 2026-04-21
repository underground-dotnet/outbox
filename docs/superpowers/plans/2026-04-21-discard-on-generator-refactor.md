# DiscardOn Generator Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `DiscardOnAttribute` parsing from runtime reflection into the source generator, generate a concrete handler-to-exception mapping keyed by handler type, and register that mapping automatically for inbox and outbox processing.

**Architecture:** Add a small runtime interface in the outbox library, refactor the runtime exception handler to depend on that interface, and extend generator handler discovery so it captures discard-on metadata while it already scans handlers. Generate one mapping implementation plus DI registration in the existing generated `ConfigureOutboxServices` output.

**Tech Stack:** C# 13 / .NET 10, Roslyn incremental generators, Microsoft.Extensions.DependencyInjection, xUnit v3, VerifyXunit, EF Core

---

## File Map

- Modify: `src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs`
  Responsibility: replace reflection lookup with generated mapping lookup.
- Create: `src/Underground.Outbox/Domain/ExceptionHandlers/IDiscardOnExceptionMapping.cs`
  Responsibility: runtime contract consumed by the exception handler and implemented by generated code.
- Modify: `src/Underground.Outbox/Configuration/ConfigureOutboxServices.cs`
  Responsibility: no behavioral rewrite expected, but confirm runtime registration assumptions still match the generated DI path.
- Modify: `src/Underground.Outbox.SourceGenerator/HandlerClassInfo.cs`
  Responsibility: extend handler discovery model to carry discard-on exception types.
- Modify: `src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs`
  Responsibility: parse `DiscardOnAttribute`, emit diagnostics if needed, generate discard mapping source, and wire generated DI registration.
- Modify: `test/Underground.Outbox.SourceGeneratorTest/OutboxGeneratorTest.cs`
  Responsibility: verify generated discard mapping for inbox/outbox handlers and omission for handlers without the attribute.
- Create or update snapshot files under `test/Underground.Outbox.SourceGeneratorTest/Snapshots/`
  Responsibility: verify generated mapping and DI output.
- Modify: `test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs`
  Responsibility: cover runtime deletion behavior when discard mappings match or do not match.
- Create: `test/Underground.OutboxTest/TestHandler/DiscardInboxMessage.cs`
  Responsibility: inbox test message type for generated discard mapping coverage.
- Create: `test/Underground.OutboxTest/TestHandler/DiscardInboxMessageHandler.cs`
  Responsibility: inbox handler decorated with `[DiscardOn]` for generator and runtime integration coverage.

### Task 1: Add the runtime discard mapping contract and refactor the exception handler

**Files:**
- Create: `src/Underground.Outbox/Domain/ExceptionHandlers/IDiscardOnExceptionMapping.cs`
- Modify: `src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs`
- Test: `test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs`

- [ ] **Step 1: Write the failing runtime tests for discard behavior**

Add tests to [ProcessorErrorTests.cs](/workspaces/outbox/test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs) that cover both matching and non-matching discard behavior before changing runtime code.

```csharp
[Fact]
public async Task DeleteMessageWhenHandlerExceptionMatchesGeneratedDiscardMapping()
{
    var serviceCollection = new ServiceCollection();

    serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
    {
        cfg.AddHandler<DiscardFailedMessageHandler>();
    });

    serviceCollection.AddBaseServices(Container, _testOutputHelper);
    var serviceProvider = serviceCollection.BuildServiceProvider();
    var context = CreateDbContext();
    var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(10));
    var outbox = serviceProvider.GetRequiredService<IOutbox>();

    await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
    {
        await outbox.AddMessageAsync(context, message, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

    var remaining = await context.Database
        .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE id = {message.Id}")
        .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal(0, remaining);
}

[Fact]
public async Task KeepMessageWhenHandlerExceptionIsNotInGeneratedDiscardMapping()
{
    var serviceCollection = new ServiceCollection();

    serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
    {
        cfg.AddHandler<FailedMessageHandler>();
    });

    serviceCollection.AddBaseServices(Container, _testOutputHelper);
    var serviceProvider = serviceCollection.BuildServiceProvider();
    var context = CreateDbContext();
    var message = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new FailedMessage(10));
    var outbox = serviceProvider.GetRequiredService<IOutbox>();

    await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
    {
        await outbox.AddMessageAsync(context, message, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    await Processor<OutboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

    var remaining = await context.Database
        .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.outbox WHERE id = {message.Id} AND processed_at IS NULL")
        .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal(1, remaining);
}
```

- [ ] **Step 2: Run the targeted runtime tests to verify the new assertions fail or expose the current reflection path**

Run: `dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter ProcessorErrorTests`

Expected: existing behavior may already pass one case through reflection, but the test run establishes the baseline before the refactor.

- [ ] **Step 3: Add the runtime contract**

Create [IDiscardOnExceptionMapping.cs](/workspaces/outbox/src/Underground.Outbox/Domain/ExceptionHandlers/IDiscardOnExceptionMapping.cs) with the minimal contract consumed by runtime code.

```csharp
namespace Underground.Outbox.Domain.ExceptionHandlers;

public interface IDiscardOnExceptionMapping
{
    IReadOnlySet<Type>? GetDiscardOnTypes(Type handlerType);
}
```

- [ ] **Step 4: Refactor the runtime exception handler to use the mapping instead of reflection**

Update [DiscardMessageOnExceptionHandler.cs](/workspaces/outbox/src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs) to inject `IDiscardOnExceptionMapping` and remove `System.Reflection`.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

public class DiscardMessageOnExceptionHandler<TEntity>(
    ILogger<DiscardMessageOnExceptionHandler<TEntity>> logger,
    IDiscardOnExceptionMapping discardOnExceptionMapping) : IMessageExceptionHandler<TEntity>
    where TEntity : class, IMessage
{
    public async Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        var discardOnTypes = discardOnExceptionMapping.GetDiscardOnTypes(ex.HandlerType);
        if (discardOnTypes is null || ex.InnerException is null)
        {
            return;
        }

        if (discardOnTypes.Any(exceptionType => exceptionType.IsInstanceOfType(ex.InnerException)))
        {
            logger.LogInformation(
                ex.InnerException,
                "Handler {HandlerType} has discard mapping for {ExceptionType}. Discarding message {MessageId}",
                ex.HandlerType.Name,
                ex.InnerException.GetType(),
                message.Id
            );

            await dbContext.Set<TEntity>()
                .Where(m => m.Id == message.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 5: Re-run the targeted runtime tests**

Run: `dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter ProcessorErrorTests`

Expected: tests still fail until generated DI and mapping source are added.

- [ ] **Step 6: Commit checkpoint**

```bash
git add src/Underground.Outbox/Domain/ExceptionHandlers/IDiscardOnExceptionMapping.cs src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs
git commit -m "refactor: replace discard reflection with mapping contract"
```

### Task 2: Extend the generator handler model to capture discard-on metadata

**Files:**
- Modify: `src/Underground.Outbox.SourceGenerator/HandlerClassInfo.cs`
- Modify: `src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs`
- Test: `test/Underground.Outbox.SourceGeneratorTest/OutboxGeneratorTest.cs`

- [ ] **Step 1: Add failing generator tests for discard metadata generation**

Append tests to [OutboxGeneratorTest.cs](/workspaces/outbox/test/Underground.Outbox.SourceGeneratorTest/OutboxGeneratorTest.cs) that exercise outbox, inbox, and no-attribute cases.

```csharp
[Fact]
public Task Generates_discard_mapping_for_outbox_handler()
{
    var driver = GeneratorTestHelper.Run("""
        using System.Data;
        using System.Threading;
        using System.Threading.Tasks;

        using Underground.Outbox;
        using Underground.Outbox.Attributes;
        using Underground.Outbox.Data;

        namespace Sample;

        public sealed record TestMessage(int Id);

        public sealed class TestHandler : IOutboxMessageHandler<TestMessage>
        {
            [DiscardOn(typeof(DataException))]
            public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """);

    return Verify(driver);
}

[Fact]
public Task Generates_discard_mapping_for_inbox_handler()
{
    var driver = GeneratorTestHelper.Run("""
        using System.Data;
        using System.Threading;
        using System.Threading.Tasks;

        using Underground.Outbox;
        using Underground.Outbox.Attributes;
        using Underground.Outbox.Data;

        namespace Sample;

        public sealed record TestMessage(int Id);

        public sealed class TestHandler : IInboxMessageHandler<TestMessage>
        {
            [DiscardOn(typeof(DataException))]
            public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """);

    return Verify(driver);
}

[Fact]
public Task Omits_discard_mapping_for_handler_without_attribute()
{
    var driver = GeneratorTestHelper.Run("""
        using System.Threading;
        using System.Threading.Tasks;

        using Underground.Outbox;
        using Underground.Outbox.Data;

        namespace Sample;

        public sealed record TestMessage(int Id);

        public sealed class TestHandler : IOutboxMessageHandler<TestMessage>
        {
            public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
        }
        """);

    return Verify(driver);
}
```

- [ ] **Step 2: Run the generator tests to capture snapshot failures**

Run: `dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj --filter OutboxGeneratorTest`

Expected: new verification snapshots fail because no discard mapping is generated yet.

- [ ] **Step 3: Extend the handler model to carry discard exception types**

Update [HandlerClassInfo.cs](/workspaces/outbox/src/Underground.Outbox.SourceGenerator/HandlerClassInfo.cs) so discard metadata travels with handler discovery results.

```csharp
internal readonly record struct HandlerClassInfo
{
    internal string HandlerFullName { get; }
    internal string MessageTypeFullName { get; }
    internal HandlerKind Kind { get; }
    internal EquatableList<string> DiscardOnExceptionTypeFullNames { get; }

    public HandlerClassInfo(
        string handlerFullName,
        string messageTypeFullName,
        HandlerKind kind,
        EquatableList<string>? discardOnExceptionTypeFullNames = null)
    {
        HandlerFullName = handlerFullName;
        MessageTypeFullName = messageTypeFullName;
        Kind = kind;
        DiscardOnExceptionTypeFullNames = discardOnExceptionTypeFullNames ?? [];
    }
}
```

- [ ] **Step 4: Add generator helpers that resolve `DiscardOnAttribute` from the concrete `HandleAsync` method**

Add helper methods to [OutboxGenerator.cs](/workspaces/outbox/src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs) for method resolution and attribute extraction.

```csharp
private static EquatableList<string> GetDiscardOnExceptionTypes(INamedTypeSymbol typeSymbol, INamedTypeSymbol handlerInterface)
{
    var result = new EquatableList<string>();
    var handleMethod = typeSymbol.GetMembers("HandleAsync")
        .OfType<IMethodSymbol>()
        .FirstOrDefault(method => SymbolEqualityComparer.Default.Equals(
            typeSymbol.FindImplementationForInterfaceMember(handlerInterface.GetMembers("HandleAsync").Single()),
            method));

    if (handleMethod is null)
    {
        return result;
    }

    foreach (var attribute in handleMethod.GetAttributes())
    {
        if (attribute.AttributeClass?.ToDisplayString() != "Underground.Outbox.Attributes.DiscardOnAttribute")
        {
            continue;
        }

        foreach (var value in attribute.ConstructorArguments)
        {
            foreach (var typeValue in value.Values)
            {
                if (typeValue.Value is ITypeSymbol exceptionType)
                {
                    result.Add(exceptionType.ToDisplayString());
                }
            }
        }
    }

    return result;
}
```

- [ ] **Step 5: Thread discard metadata through handler discovery**

Update the `GetHandlerInfos` logic in [OutboxGenerator.cs](/workspaces/outbox/src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs) so each yielded `HandlerClassInfo` includes discard exception types.

```csharp
if (originalDef.StartsWith(OutboxHandlerInterface, StringComparison.Ordinal))
{
    yield return new HandlerClassInfo(
        typeSymbol.ToDisplayString(),
        iface.TypeArguments[0].ToDisplayString(),
        HandlerKind.Outbox,
        GetDiscardOnExceptionTypes(typeSymbol, iface)
    );
    continue;
}

if (originalDef.StartsWith(InboxHandlerInterface, StringComparison.Ordinal))
{
    yield return new HandlerClassInfo(
        typeSymbol.ToDisplayString(),
        iface.TypeArguments[0].ToDisplayString(),
        HandlerKind.Inbox,
        GetDiscardOnExceptionTypes(typeSymbol, iface)
    );
}
```

- [ ] **Step 6: Re-run generator tests**

Run: `dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj --filter OutboxGeneratorTest`

Expected: tests still fail until the generator emits the mapping source and DI registration.

- [ ] **Step 7: Commit checkpoint**

```bash
git add src/Underground.Outbox.SourceGenerator/HandlerClassInfo.cs src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs test/Underground.Outbox.SourceGeneratorTest/OutboxGeneratorTest.cs
git commit -m "feat: capture discard metadata in handler discovery"
```

### Task 3: Generate the discard mapping implementation and DI registration

**Files:**
- Modify: `src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs`
- Test: `test/Underground.Outbox.SourceGeneratorTest/OutboxGeneratorTest.cs`
- Test: `test/Underground.Outbox.SourceGeneratorTest/Snapshots/*.verified.cs`

- [ ] **Step 1: Generate failing snapshots for the new mapping and DI registration**

Keep the new tests from Task 2 and confirm the snapshots are missing the generated mapping service and interface registration.

Run: `dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj --filter OutboxGeneratorTest`

Expected: snapshot output shows missing `GeneratedDiscardOnExceptionMapping` and missing `IDiscardOnExceptionMapping` registration in generated DI.

- [ ] **Step 2: Emit the generated discard mapping source**

Add a second generated source in [OutboxGenerator.cs](/workspaces/outbox/src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs) that emits only handlers with discard metadata.

```csharp
private static void GenerateDiscardOnMapping(EquatableList<HandlerClassInfo> handlers, SourceProductionContext context)
{
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated/>");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine();
    sb.AppendLine("using Underground.Outbox.Domain.ExceptionHandlers;");
    sb.AppendLine();
    sb.AppendLine("namespace Underground.Outbox.Configuration;");
    sb.AppendLine();
    sb.AppendLine("public sealed class GeneratedDiscardOnExceptionMapping : IDiscardOnExceptionMapping");
    sb.AppendLine("{");
    sb.AppendLine("    public IReadOnlySet<Type>? GetDiscardOnTypes(Type handlerType)");
    sb.AppendLine("    {");

    foreach (var handler in handlers.Where(h => h.DiscardOnExceptionTypeFullNames.Count > 0))
    {
        sb.AppendLine($"        if (handlerType == typeof({handler.HandlerFullName}))");
        sb.AppendLine("        {");
        sb.AppendLine("            return new HashSet<Type>");
        sb.AppendLine("            {");
        foreach (var exceptionType in handler.DiscardOnExceptionTypeFullNames)
        {
            sb.AppendLine($"                typeof({exceptionType}),");
        }
        sb.AppendLine("            };");
        sb.AppendLine("        }");
    }

    sb.AppendLine("        return null;");
    sb.AppendLine("    }");
    sb.AppendLine("}");

    context.AddSource("DiscardOnMapping.g.cs", sb.ToString());
}
```

- [ ] **Step 3: Register the generated mapping in generated DI**

Update the `GenerateDIMethod` string in [OutboxGenerator.cs](/workspaces/outbox/src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs) so both add methods include the mapping registration.

```csharp
services.AddScoped<IMessageDispatcher<OutboxMessage>, GeneratedDispatcher<OutboxMessage>>();
services.AddScoped<IDiscardOnExceptionMapping, GeneratedDiscardOnExceptionMapping>();
SetupOutboxServices.SetupInternalOutboxServices<TContext>(services, configuration);
```

```csharp
services.AddScoped<IMessageDispatcher<InboxMessage>, GeneratedDispatcher<InboxMessage>>();
services.AddScoped<IDiscardOnExceptionMapping, GeneratedDiscardOnExceptionMapping>();
SetupOutboxServices.SetupInternalInboxServices<TContext>(services, configuration);
```

Also add the missing using inside the generated DI source text:

```csharp
using Underground.Outbox.Domain.ExceptionHandlers;
```

- [ ] **Step 4: Invoke discard mapping generation from the main execution path**

Update `Execute` in [OutboxGenerator.cs](/workspaces/outbox/src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs) to emit both sources.

```csharp
private static void Execute(EquatableList<HandlerClassInfo> handlers, SourceProductionContext context)
{
    GenerateDispatcher(handlers, context);
    GenerateDiscardOnMapping(handlers, context);
}
```

If needed, split the current dispatcher emission block into a dedicated `GenerateDispatcher` method before adding the mapping generator.

- [ ] **Step 5: Re-run generator tests and accept snapshot updates**

Run: `dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj --filter OutboxGeneratorTest`

Expected: PASS after updating snapshots under `test/Underground.Outbox.SourceGeneratorTest/Snapshots/` to include the generated mapping and DI registration output.

- [ ] **Step 6: Commit checkpoint**

```bash
git add src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs test/Underground.Outbox.SourceGeneratorTest/OutboxGeneratorTest.cs test/Underground.Outbox.SourceGeneratorTest/Snapshots
git commit -m "feat: generate discard exception mapping"
```

### Task 4: Add inbox coverage and end-to-end runtime verification

**Files:**
- Create: `test/Underground.OutboxTest/TestHandler/DiscardInboxMessage.cs`
- Create: `test/Underground.OutboxTest/TestHandler/DiscardInboxMessageHandler.cs`
- Modify: `test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs`
- Test: `test/Underground.OutboxTest/Underground.OutboxTest.csproj`

- [ ] **Step 1: Add inbox-specific test fixtures**

Create [DiscardInboxMessage.cs](/workspaces/outbox/test/Underground.OutboxTest/TestHandler/DiscardInboxMessage.cs):

```csharp
namespace Underground.OutboxTest.TestHandler;

public sealed record DiscardInboxMessage(int Id);
```

Create [DiscardInboxMessageHandler.cs](/workspaces/outbox/test/Underground.OutboxTest/TestHandler/DiscardInboxMessageHandler.cs):

```csharp
using System.Data;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public sealed class DiscardInboxMessageHandler : IInboxMessageHandler<DiscardInboxMessage>
{
    [DiscardOn(typeof(DataException))]
    public Task HandleAsync(DiscardInboxMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new DataException("Failed to handle inbox message");
    }
}
```

- [ ] **Step 2: Add a failing inbox runtime test**

Extend [ProcessorErrorTests.cs](/workspaces/outbox/test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs) with an inbox processing test that proves the same generated mapping works for inbox handlers.

```csharp
[Fact]
public async Task DeleteInboxMessageWhenHandlerExceptionMatchesGeneratedDiscardMapping()
{
    var serviceCollection = new ServiceCollection();

    serviceCollection.AddInboxServices<TestDbContext>(cfg =>
    {
        cfg.AddHandler<DiscardInboxMessageHandler>();
    });

    serviceCollection.AddBaseServices(Container, _testOutputHelper);
    var serviceProvider = serviceCollection.BuildServiceProvider();
    var context = CreateDbContext();
    var message = new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardInboxMessage(10));
    var inbox = serviceProvider.GetRequiredService<IInbox>();

    await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
    {
        await inbox.AddMessageAsync(context, message, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    await Processor<InboxMessage>.ProcessWithDefaultValues(serviceProvider, TestContext.Current.CancellationToken);

    var remaining = await context.Database
        .SqlQuery<int>($"SELECT COUNT(id) AS \"Value\" FROM public.inbox WHERE id = {message.Id}")
        .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal(0, remaining);
}
```

- [ ] **Step 3: Run the focused runtime suite**

Run: `dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter ProcessorErrorTests`

Expected: PASS once generated DI registration and mapping are working for both pipelines.

- [ ] **Step 4: Run the full source generator and runtime test projects**

Run: `dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj`
Expected: PASS

Run: `dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj`
Expected: PASS

- [ ] **Step 5: Commit checkpoint**

```bash
git add test/Underground.OutboxTest/TestHandler/DiscardInboxMessage.cs test/Underground.OutboxTest/TestHandler/DiscardInboxMessageHandler.cs test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs
git commit -m "test: cover discard mapping for inbox and outbox"
```

### Task 5: Final verification and cleanup

**Files:**
- Modify: `src/Underground.Outbox.SourceGenerator/OutboxGenerator.cs`
- Modify: `src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs`
- Test: `test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj`
- Test: `test/Underground.OutboxTest/Underground.OutboxTest.csproj`

- [ ] **Step 1: Check for stale reflection-based code or dead imports**

Verify the runtime handler no longer references reflection:

Run: `grep -R "System.Reflection\\|GetCustomAttribute<DiscardOnAttribute>\\|GetMethod(\"HandleAsync\")" -n src/Underground.Outbox src/Underground.Outbox.SourceGenerator`

Expected: no reflection-based discard lookup remains in runtime code.

- [ ] **Step 2: Run a full repository test pass for the touched projects**

Run: `dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj && dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj`

Expected: all tests pass.

- [ ] **Step 3: Inspect generated output in a consumer project if snapshots are unclear**

Run: `dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter ProcessorErrorTests`

Expected: the compiler-generated files under `test/Underground.OutboxTest/obj/Debug/net10.0/generated/Underground.Outbox.SourceGenerator/` include `DiscardOnMapping.g.cs` with handler entries and `OutboxDependencyInjection.g.cs` with `IDiscardOnExceptionMapping` registration.

- [ ] **Step 4: Commit final integration**

```bash
git add src/Underground.Outbox src/Underground.Outbox.SourceGenerator test/Underground.Outbox.SourceGeneratorTest test/Underground.OutboxTest
git commit -m "feat: generate discard mappings for inbox and outbox handlers"
```

## Self-Review

- Spec coverage:
  - Runtime reflection removal is covered by Task 1.
  - Shared runtime interface is covered by Task 1.
  - Generator parsing of `DiscardOnAttribute` is covered by Task 2.
  - Generated mapping and DI registration are covered by Task 3.
  - Inbox and outbox support plus runtime verification are covered by Task 4.
  - Final verification and stale-code cleanup are covered by Task 5.
- Placeholder scan:
  - No `TODO`, `TBD`, or deferred “write tests later” steps remain.
  - Each code-changing step includes concrete code blocks or explicit commands.
- Type consistency:
  - The plan consistently uses `IDiscardOnExceptionMapping`, `GeneratedDiscardOnExceptionMapping`, and `GetDiscardOnTypes(Type handlerType)`.
  - Generated DI wiring uses `Underground.Outbox.Domain.ExceptionHandlers`, matching the proposed runtime interface location.
