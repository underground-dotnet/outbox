using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// Two modules claiming one message type cannot be seen by a single compilation, so OUTBOX001 cannot
/// report it. The registry is where that pair is caught instead, as the host starts.
/// </summary>
public class HandlerRegistryTests
{
    private static HandlerEntry<OutboxMessage> Entry(string messageTypeName, Type handlerType) =>
        new(messageTypeName, handlerType, typeof(ExampleMessage), (_, _, _, _) => Task.CompletedTask);

    [Fact]
    public void Throws_WhenTwoHandlersClaimTheSameMessageType()
    {
        // Arrange
        HandlerEntry<OutboxMessage>[] entries =
        [
            Entry("Sample.Message", typeof(ExampleMessageHandler)),
            Entry("Sample.Message", typeof(SecondMessageHandler)),
        ];

        // Act & Assert
        var exception = Assert.Throws<CompetingHandlersException>(() => new HandlerRegistry<OutboxMessage>(entries));

        Assert.Equal("Sample.Message", exception.MessageTypeName);
        Assert.Contains(nameof(ExampleMessageHandler), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SecondMessageHandler), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The registry is otherwise built by the first claim, where the worker's catch-all would log the throw
    /// on every poll. Resolving the hosted services is the step in which the host fails to start.
    /// </summary>
    [Fact]
    public void FailsHostStart_WhenTwoHandlersClaimTheSameMessageType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxServices<TestDbContext>(_ => { });
        services.AddSingleton(Entry("Sample.Message", typeof(ExampleMessageHandler)));
        services.AddSingleton(Entry("Sample.Message", typeof(SecondMessageHandler)));

        using var provider = services.BuildServiceProvider();

        // Act & Assert
        Assert.Throws<CompetingHandlersException>(() => provider.GetRequiredService<IEnumerable<IHostedService>>());
    }

    /// <summary>Calling one module's generated method twice contributes its entries twice.</summary>
    [Fact]
    public void Accepts_TheSameHandlerContributedTwice()
    {
        // Arrange
        HandlerEntry<OutboxMessage>[] entries =
        [
            Entry("Sample.Message", typeof(ExampleMessageHandler)),
            Entry("Sample.Message", typeof(ExampleMessageHandler)),
        ];

        // Act
        var registry = new HandlerRegistry<OutboxMessage>(entries);

        // Assert
        Assert.True(registry.TryGetEntry("Sample.Message", out _));
    }

    [Fact]
    public void DoesNotFindAnEntry_ForAnUnclaimedMessageType()
    {
        // Arrange
        var registry = new HandlerRegistry<OutboxMessage>([Entry("Sample.Message", typeof(ExampleMessageHandler))]);

        // Act
        var found = registry.TryGetEntry("Sample.Other", out _);

        // Assert
        Assert.False(found);
    }

    /// <summary>
    /// A handler of several message types is registered once as its concrete type, so the lifetime it
    /// declares governs the handler rather than each message type it handles.
    /// </summary>
    [Fact]
    public void AScopedHandlerOfSeveralMessageTypes_IsOneInstancePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddUndergroundOutboxTestMessageHandlers();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var asNested = scope.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Envelope.Nested>>();
        var asWrapped = scope.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Wrapped<Envelope.Nested>>>();

        // Assert
        Assert.Same(asNested, asWrapped);
    }

    [Fact]
    public void AScopedHandler_IsADifferentInstanceInAnotherScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddUndergroundOutboxTestMessageHandlers();

        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // Act
        var one = first.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Envelope.Nested>>();
        var other = second.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Envelope.Nested>>();

        // Assert
        Assert.NotSame(one, other);
    }
}
