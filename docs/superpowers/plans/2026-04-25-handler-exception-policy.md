# Handler Exception Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `DiscardOnAttribute` reflection with registration-time handler exception policies exposed through `AddHandler(...).OnException<T>().Discard()`.

**Architecture:** Extend `ServiceConfiguration` from a plain DI descriptor list into a handler registration model with immutable exception policy metadata. Keep dispatching unchanged, build a runtime policy registry during service setup, and make `DiscardMessageOnExceptionHandler` resolve actions from that registry using most-specific assignable exception matching.

**Tech Stack:** .NET 9, C#, EF Core, Microsoft.Extensions.DependencyInjection, xUnit

---

## File Map

### Create

- `src/Underground.Outbox/Configuration/HandlerExceptionAction.cs`
  Enum for runtime actions such as `Discard`.
- `src/Underground.Outbox/Configuration/HandlerExceptionPolicy.cs`
  Immutable rule record containing `ExceptionType` and action.
- `src/Underground.Outbox/Configuration/HandlerRegistration.cs`
  Registration model containing service type, implementation type, lifetime, and exception policies.
- `src/Underground.Outbox/Configuration/HandlerRegistrationBuilder.cs`
  Fluent builder returned from `AddHandler<THandler>()`.
- `src/Underground.Outbox/Configuration/HandlerExceptionPolicyBuilder.cs`
  Builder returned by `OnException<TException>()`.
- `src/Underground.Outbox/Configuration/HandlerExceptionPolicyRegistry.cs`
  Immutable runtime lookup and most-specific-match selector.
- `test/Underground.OutboxTest/Configuration/ServiceConfigurationTests.cs`
  Unit tests for registration, validation, and fluent builder behavior.
- `test/Underground.OutboxTest/TestHandler/DerivedDataException.cs`
  Test exception used to verify subclass matching.

### Modify

- `src/Underground.Outbox/Configuration/ServiceConfiguration.cs`
  Return fluent builders, store `HandlerRegistration`, validate policies.
- `src/Underground.Outbox/Configuration/OutboxServiceConfiguration.cs`
  Resolve one outbox handler interface and create registrations.
- `src/Underground.Outbox/Configuration/InboxServiceConfiguration.cs`
  Resolve one inbox handler interface and create registrations.
- `src/Underground.Outbox/Configuration/ConfigureOutboxServices.cs`
  Register handlers from `HandlerRegistration` and add the policy registry to DI.
- `src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs`
  Replace reflection with registry lookup.
- `src/Underground.Outbox/Attributes/DiscardOnAttribute.cs`
  Remove or obsolete, depending on final cleanup choice.
- `test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs`
  Migrate discard tests to the fluent API and add policy selection coverage.
- `test/Underground.OutboxTest/TestHandler/DiscardFailedMessageHandler.cs`
  Remove the attribute.
- `README.md`
  Update examples to use fluent exception policy registration.
- `example/ConsoleApp/Program.cs`
  Show the new registration style.

---

### Task 1: Introduce the registration model and fluent builders

**Files:**
- Create: `src/Underground.Outbox/Configuration/HandlerExceptionAction.cs`
- Create: `src/Underground.Outbox/Configuration/HandlerExceptionPolicy.cs`
- Create: `src/Underground.Outbox/Configuration/HandlerRegistration.cs`
- Create: `src/Underground.Outbox/Configuration/HandlerRegistrationBuilder.cs`
- Create: `src/Underground.Outbox/Configuration/HandlerExceptionPolicyBuilder.cs`
- Modify: `src/Underground.Outbox/Configuration/ServiceConfiguration.cs`
- Modify: `src/Underground.Outbox/Configuration/OutboxServiceConfiguration.cs`
- Modify: `src/Underground.Outbox/Configuration/InboxServiceConfiguration.cs`
- Test: `test/Underground.OutboxTest/Configuration/ServiceConfigurationTests.cs`

- [ ] **Step 1: Write the failing configuration tests**

```csharp
using System.Data;

using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Configuration;

public class ServiceConfigurationTests
{
    [Fact]
    public void AddHandler_ReturnsBuilderThatStoresDiscardRule()
    {
        var config = new OutboxServiceConfiguration();

        config.AddHandler<DiscardFailedMessageHandler>()
            .OnException<DataException>().Discard();

        var registration = Assert.Single(config.HandlerRegistrations);
        Assert.Equal(typeof(IOutboxMessageHandler<DiscardMessage>), registration.ServiceType);
        Assert.Equal(typeof(DiscardFailedMessageHandler), registration.ImplementationType);

        var policy = Assert.Single(registration.ExceptionPolicies);
        Assert.Equal(typeof(DataException), policy.ExceptionType);
        Assert.Equal(HandlerExceptionAction.Discard, policy.Action);
    }

    [Fact]
    public void Validate_Throws_WhenHandlerDoesNotImplementCurrentScopeInterface()
    {
        var config = new InboxServiceConfiguration();

        Assert.Throws<ArgumentException>(() => config.AddHandler<DiscardFailedMessageHandler>());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter FullyQualifiedName~ServiceConfigurationTests
```

Expected: FAIL with compile errors because `HandlerRegistrations`, `OnException`, `Discard`, and the new policy types do not exist yet.

- [ ] **Step 3: Add the registration and builder types**

Create the core configuration types with these shapes:

```csharp
namespace Underground.Outbox.Configuration;

internal enum HandlerExceptionAction
{
    Discard
}
```

```csharp
namespace Underground.Outbox.Configuration;

internal sealed record HandlerExceptionPolicy(Type ExceptionType, HandlerExceptionAction Action);
```

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

internal sealed class HandlerRegistration(
    Type serviceType,
    Type implementationType,
    ServiceLifetime serviceLifetime)
{
    public Type ServiceType { get; } = serviceType;
    public Type ImplementationType { get; } = implementationType;
    public ServiceLifetime ServiceLifetime { get; } = serviceLifetime;
    public List<HandlerExceptionPolicy> ExceptionPolicies { get; } = [];

    public ServiceDescriptor ToServiceDescriptor() => new(ServiceType, ImplementationType, ServiceLifetime);
}
```

```csharp
namespace Underground.Outbox.Configuration;

public sealed class HandlerRegistrationBuilder
{
    private readonly HandlerRegistration _registration;

    internal HandlerRegistrationBuilder(HandlerRegistration registration)
    {
        _registration = registration;
    }

    public HandlerExceptionPolicyBuilder OnException<TException>() where TException : Exception
    {
        return new HandlerExceptionPolicyBuilder(this, typeof(TException));
    }

    internal void AddPolicy(Type exceptionType, HandlerExceptionAction action)
    {
        _registration.ExceptionPolicies.Add(new HandlerExceptionPolicy(exceptionType, action));
    }
}
```

```csharp
namespace Underground.Outbox.Configuration;

public sealed class HandlerExceptionPolicyBuilder
{
    private readonly HandlerRegistrationBuilder _builder;
    private readonly Type _exceptionType;

    internal HandlerExceptionPolicyBuilder(HandlerRegistrationBuilder builder, Type exceptionType)
    {
        _builder = builder;
        _exceptionType = exceptionType;
    }

    public HandlerRegistrationBuilder Discard()
    {
        _builder.AddPolicy(_exceptionType, HandlerExceptionAction.Discard);
        return _builder;
    }
}
```

- [ ] **Step 4: Update `ServiceConfiguration` and scope-specific registration**

Change the configuration API from `ServiceDescriptor` storage to `HandlerRegistration` storage:

```csharp
public abstract class ServiceConfiguration
{
    internal List<HandlerRegistration> HandlerRegistrations { get; } = [];

    public HandlerRegistrationBuilder AddHandler<TMessageHandlerType>()
    {
        return AddHandler(typeof(TMessageHandlerType));
    }

    public HandlerRegistrationBuilder AddHandler<TMessageHandlerType>(ServiceLifetime serviceLifetime)
    {
        return AddHandler(typeof(TMessageHandlerType), serviceLifetime);
    }

    public abstract HandlerRegistrationBuilder AddHandler(
        HandlerType messageHandlerType,
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient);

    internal void Validate()
    {
        if (BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException($"BatchSize ({BatchSize}) must be greater than 0.");
        }

        if (ParallelProcessingOfPartitions <= 0)
        {
            throw new ArgumentOutOfRangeException($"ParallelProcessingOfPartitions ({ParallelProcessingOfPartitions}) must be greater than 0.");
        }

        if (ProcessingDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException($"ProcessingDelayMilliseconds ({ProcessingDelayMilliseconds}) cannot be negative.");
        }

        foreach (var registration in HandlerRegistrations)
        {
            foreach (var policy in registration.ExceptionPolicies)
            {
                if (!typeof(Exception).IsAssignableFrom(policy.ExceptionType))
                {
                    throw new ArgumentException($"Configured type {policy.ExceptionType} must derive from Exception.");
                }
            }
        }
    }
}
```

Create one registration per handler and throw on invalid scope registration:

```csharp
public override HandlerRegistrationBuilder AddHandler(
    HandlerType messageHandlerType,
    ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
{
    var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IOutboxMessageHandler`1");
    if (interfaceType?.IsGenericType != true)
    {
        throw new ArgumentException($"{messageHandlerType} does not implement IOutboxMessageHandler<T>.");
    }

    var registration = new HandlerRegistration(interfaceType, messageHandlerType, serviceLifetime);
    HandlerRegistrations.Add(registration);
    return new HandlerRegistrationBuilder(registration);
}
```

Mirror the same pattern in `InboxServiceConfiguration` using `IInboxMessageHandler<T>`.

- [ ] **Step 5: Run the tests to verify they pass**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter FullyQualifiedName~ServiceConfigurationTests
```

Expected: PASS with both configuration tests green.

- [ ] **Step 6: Commit**

```bash
git add src/Underground.Outbox/Configuration test/Underground.OutboxTest/Configuration
git commit -m "feat: add fluent handler exception registration"
```

### Task 2: Add the immutable runtime policy registry

**Files:**
- Create: `src/Underground.Outbox/Configuration/HandlerExceptionPolicyRegistry.cs`
- Modify: `src/Underground.Outbox/Configuration/ServiceConfiguration.cs`
- Test: `test/Underground.OutboxTest/Configuration/ServiceConfigurationTests.cs`

- [ ] **Step 1: Write the failing registry tests**

Extend `ServiceConfigurationTests.cs` with matching and ambiguity coverage:

```csharp
[Fact]
public void Registry_SelectsMostSpecificMatchingPolicy()
{
    var registry = new HandlerExceptionPolicyRegistry(
        new Dictionary<Type, IReadOnlyList<HandlerExceptionPolicy>>
        {
            [typeof(DiscardFailedMessageHandler)] =
            [
                new(typeof(Exception), HandlerExceptionAction.Discard),
                new(typeof(DataException), HandlerExceptionAction.Discard)
            ]
        });

    var match = registry.Find(typeof(DiscardFailedMessageHandler), new DerivedDataException("boom"));

    Assert.NotNull(match);
    Assert.Equal(typeof(DataException), match!.ExceptionType);
}

[Fact]
public void Validate_Throws_WhenTwoRulesUseTheSameExceptionType()
{
    var config = new OutboxServiceConfiguration();

    config.AddHandler<DiscardFailedMessageHandler>()
        .OnException<DataException>().Discard()
        .OnException<DataException>().Discard();

    Assert.Throws<ArgumentException>(() => config.Validate());
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter FullyQualifiedName~ServiceConfigurationTests
```

Expected: FAIL because `HandlerExceptionPolicyRegistry`, `Find`, and duplicate-policy validation do not exist yet.

- [ ] **Step 3: Implement the immutable registry and duplicate validation**

Create the registry with a small lookup API:

```csharp
namespace Underground.Outbox.Configuration;

internal sealed class HandlerExceptionPolicyRegistry
{
    private readonly IReadOnlyDictionary<Type, IReadOnlyList<HandlerExceptionPolicy>> _policies;

    public HandlerExceptionPolicyRegistry(IEnumerable<HandlerRegistration> registrations)
    {
        _policies = registrations.ToDictionary(
            registration => registration.ImplementationType,
            registration => (IReadOnlyList<HandlerExceptionPolicy>)registration.ExceptionPolicies.ToArray());
    }

    internal HandlerExceptionPolicyRegistry(IReadOnlyDictionary<Type, IReadOnlyList<HandlerExceptionPolicy>> policies)
    {
        _policies = policies;
    }

    public HandlerExceptionPolicy? Find(Type handlerType, Exception exception)
    {
        if (!_policies.TryGetValue(handlerType, out var policies))
        {
            return null;
        }

        return policies
            .Where(policy => policy.ExceptionType.IsAssignableFrom(exception.GetType()))
            .OrderBy(policy => GetDistance(exception.GetType(), policy.ExceptionType))
            .FirstOrDefault();
    }

    private static int GetDistance(Type thrownType, Type configuredType)
    {
        var distance = 0;
        for (var current = thrownType; current is not null; current = current.BaseType)
        {
            if (current == configuredType)
            {
                return distance;
            }

            distance++;
        }

        return int.MaxValue;
    }
}
```

Add duplicate validation to `ServiceConfiguration.Validate()`:

```csharp
foreach (var registration in HandlerRegistrations)
{
    var duplicate = registration.ExceptionPolicies
        .GroupBy(policy => policy.ExceptionType)
        .FirstOrDefault(group => group.Count() > 1);

    if (duplicate is not null)
    {
        throw new ArgumentException(
            $"Handler {registration.ImplementationType} has duplicate exception policy for {duplicate.Key}.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter FullyQualifiedName~ServiceConfigurationTests
```

Expected: PASS with the registry-selection and duplicate-policy tests green.

- [ ] **Step 5: Commit**

```bash
git add src/Underground.Outbox/Configuration test/Underground.OutboxTest/Configuration
git commit -m "feat: add handler exception policy registry"
```

### Task 3: Wire the registry into service setup and replace reflection-based discard handling

**Files:**
- Modify: `src/Underground.Outbox/Configuration/ConfigureOutboxServices.cs`
- Modify: `src/Underground.Outbox/Domain/ExceptionHandlers/DiscardMessageOnExceptionHandler.cs`
- Modify: `test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs`
- Create: `test/Underground.OutboxTest/TestHandler/DerivedDataException.cs`
- Modify: `test/Underground.OutboxTest/TestHandler/DiscardFailedMessageHandler.cs`

- [ ] **Step 1: Write the failing processor tests**

Replace the attribute-based discard registration and add a subclass-match case:

```csharp
[Fact]
public async Task DiscardMessagesOnSpecificException()
{
    var serviceCollection = new ServiceCollection();

    serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
    {
        cfg.AddHandler<DiscardFailedMessageHandler>()
            .OnException<DataException>().Discard();
    });

    serviceCollection.AddBaseServices(Container, _testOutputHelper);
    var serviceProvider = serviceCollection.BuildServiceProvider();
    var context = CreateDbContext();
    var msg = new OutboxMessage(Guid.NewGuid(), DateTime.UtcNow, new DiscardMessage(10));
    var outbox = serviceProvider.GetRequiredService<IOutbox>();
    var processor = serviceProvider.GetRequiredService<SynchronousProcessor<OutboxMessage>>();

    await using (var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
    {
        await outbox.AddMessageAsync(context, msg, TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    await processor.ProcessAndWaitAsync(TestContext.Current.CancellationToken);

    Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync(cancellationToken: TestContext.Current.CancellationToken));
}

[Fact]
public async Task DiscardMessagesOnDerivedException()
{
    var serviceCollection = new ServiceCollection();

    serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
    {
        cfg.AddHandler<DiscardFailedMessageHandler>()
            .OnException<DataException>().Discard();
    });

    serviceCollection.AddBaseServices(Container, _testOutputHelper);
    var serviceProvider = serviceCollection.BuildServiceProvider();
    var registry = serviceProvider.GetRequiredService<HandlerExceptionPolicyRegistry>();

    var match = registry.Find(typeof(DiscardFailedMessageHandler), new DerivedDataException("boom"));

    Assert.NotNull(match);
    Assert.Equal(typeof(DataException), match!.ExceptionType);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter FullyQualifiedName~ProcessorErrorTests
```

Expected: FAIL because the runtime service graph still uses reflection and the test handler still depends on `DiscardOnAttribute`.

- [ ] **Step 3: Register the runtime registry and remove reflection**

In `ConfigureOutboxServices.cs`, register handlers from `HandlerRegistrations` and add the registry:

```csharp
private static void AddGenericServices<TEntity, TContext>(this IServiceCollection services, ServiceConfiguration serviceConfig)
    where TEntity : class, IMessage
    where TContext : IDbContext
{
    services.AddSingleton(serviceConfig);
    services.TryAddEnumerable(serviceConfig.HandlerRegistrations.Select(registration => registration.ToServiceDescriptor()));
    services.AddSingleton(new HandlerExceptionPolicyRegistry(serviceConfig.HandlerRegistrations));

    services.AddScoped<FetchPartitions<TEntity>>();
    services.AddScoped<FetchMessages<TEntity>>();
    services.AddSingleton<ConcurrentProcessor<TEntity>>();
    services.AddScoped<IMessageExceptionHandler<TEntity>, DiscardMessageOnExceptionHandler<TEntity>>();
    services.AddScoped<ProcessExceptionFromHandler<TEntity>>();
    services.AddScoped<Processor<TEntity>>();
    services.AddHostedService<BackgroundService<TEntity>>();
}
```

In `DiscardMessageOnExceptionHandler.cs`, remove `System.Reflection` and use the registry:

```csharp
public class DiscardMessageOnExceptionHandler<TEntity>(
    ILogger<DiscardMessageOnExceptionHandler<TEntity>> logger,
    HandlerExceptionPolicyRegistry registry) : IMessageExceptionHandler<TEntity>
    where TEntity : class, IMessage
{
    public async Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        if (ex.InnerException is null)
        {
            return;
        }

        var policy = registry.Find(ex.HandlerType, ex.InnerException);
        if (policy?.Action != HandlerExceptionAction.Discard)
        {
            return;
        }

        logger.LogInformation(
            ex.InnerException,
            "Handler {HandlerType} is configured to discard for {ExceptionType}. Discarding message {MessageId}",
            ex.HandlerType.Name,
            policy.ExceptionType,
            message.Id);

        await dbContext.Set<TEntity>()
            .Where(m => m.Id == message.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
```

Update `DiscardFailedMessageHandler.cs` to remove the attribute and keep only the throw:

```csharp
using System.Data;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class DiscardFailedMessageHandler : IOutboxMessageHandler<DiscardMessage>
{
    public Task HandleAsync(DiscardMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new DataException("Failed to handle message");
    }
}
```

Create the subclass exception used by tests:

```csharp
using System.Data;

namespace Underground.OutboxTest.TestHandler;

public sealed class DerivedDataException(string message) : DataException(message);
```

- [ ] **Step 4: Run the processor tests to verify they pass**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj --filter FullyQualifiedName~ProcessorErrorTests
```

Expected: PASS with discard now driven by fluent registration instead of reflection.

- [ ] **Step 5: Commit**

```bash
git add src/Underground.Outbox/Configuration src/Underground.Outbox/Domain/ExceptionHandlers test/Underground.OutboxTest/Domain test/Underground.OutboxTest/TestHandler
git commit -m "refactor: remove reflection from discard exception handling"
```

### Task 4: Remove attribute usage and update docs and examples

**Files:**
- Modify: `README.md`
- Modify: `example/ConsoleApp/Program.cs`
- Modify: `src/Underground.Outbox/Attributes/DiscardOnAttribute.cs`
- Modify: `test/Underground.OutboxTest/Domain/ProcessorErrorTests.cs`

- [ ] **Step 1: Write the failing documentation and API-cleanup assertions**

Add a compile-level cleanup expectation by deleting the last attribute usage from tests and examples, then verify the full solution still builds:

```bash
dotnet test Underground.slnx
```

Expected: FAIL until the README/example code and attribute cleanup are consistent with the new API.

- [ ] **Step 2: Update docs and examples**

Replace attribute guidance with fluent registration in `README.md` and `example/ConsoleApp/Program.cs`:

```csharp
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler>()
       .OnException<DataException>().Discard();
});
```

If you want the example to stay simpler than the discard feature, keep `ExampleMessageHandler` as a bare `AddHandler<ExampleMessageHandler>();` and add a short README subsection showing the new fluent exception policy API instead:

```csharp
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler>();
    cfg.AddHandler<DiscardFailedMessageHandler>()
       .OnException<DataException>().Discard();
});
```

- [ ] **Step 3: Remove or obsolete the attribute**

Preferred cleanup:

```csharp
// Delete src/Underground.Outbox/Attributes/DiscardOnAttribute.cs
```

If you want one release of migration padding instead, mark it obsolete:

```csharp
[Obsolete("Use AddHandler(...).OnException<TException>().Discard() during service registration.")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class DiscardOnAttribute : Attribute
{
    public DiscardOnAttribute(params Type[] exceptionTypes) { }
}
```

Choose one approach and apply it consistently. Do not leave runtime code referencing the attribute.

- [ ] **Step 4: Run the full test suite**

Run:

```bash
dotnet test Underground.slnx
```

Expected: PASS with the updated API, examples, and runtime behavior.

- [ ] **Step 5: Commit**

```bash
git add README.md example/ConsoleApp/Program.cs src/Underground.Outbox/Attributes test/Underground.OutboxTest
git commit -m "docs: migrate discard policy to fluent registration"
```

### Task 5: Final verification and package-level sanity check

**Files:**
- Modify: `README.md` if test output reveals stale snippets
- Modify: `docs/superpowers/specs/2026-04-25-handler-exception-policy-design.md` only if implementation meaningfully diverged

- [ ] **Step 1: Run focused source-generator tests**

Run:

```bash
dotnet test test/Underground.Outbox.SourceGeneratorTest/Underground.Outbox.SourceGeneratorTest.csproj
```

Expected: PASS. The generator should not need functional changes because it still throws `MessageHandlerException(handler.GetType(), ...)`.

- [ ] **Step 2: Run the package test project one more time**

Run:

```bash
dotnet test test/Underground.OutboxTest/Underground.OutboxTest.csproj
```

Expected: PASS across configuration, processor, and integration tests.

- [ ] **Step 3: Inspect the public surface for accidental leaks**

Verify that only the intended fluent types are public and internals stay internal:

```bash
grep -RIn "public sealed class HandlerRegistrationBuilder\\|public sealed class HandlerExceptionPolicyBuilder\\|internal sealed class HandlerRegistration\\|internal sealed class HandlerExceptionPolicyRegistry" src/Underground.Outbox
```

Expected: the builder types are public, while registration and registry internals remain internal.

- [ ] **Step 4: Commit the verification pass**

```bash
git add src/Underground.Outbox README.md docs/superpowers/specs/2026-04-25-handler-exception-policy-design.md
git commit -m "test: verify fluent handler exception policy rollout"
```

## Self-Review

- Spec coverage:
  - registration-time policy model: Task 1
  - immutable registry and specificity selection: Task 2
  - runtime exception handling without reflection: Task 3
  - no-match behavior preserved: Task 3 integration tests
  - docs and migration: Task 4
  - generator remains unchanged but verified: Task 5
- Placeholder scan:
  - no `TODO`, `TBD`, or generic “write tests” placeholders remain
  - each task includes exact files, commands, and code shapes
- Type consistency:
  - `HandlerRegistration`, `HandlerExceptionPolicy`, `HandlerExceptionAction`, `HandlerExceptionPolicyRegistry`, `HandlerRegistrationBuilder`, and `HandlerExceptionPolicyBuilder` are used consistently throughout
