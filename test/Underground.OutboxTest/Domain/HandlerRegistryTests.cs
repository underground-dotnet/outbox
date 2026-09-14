using Microsoft.Extensions.DependencyInjection;

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
        HandlerEntry<OutboxMessage>[] entries =
        [
            Entry("Sample.Message", typeof(ExampleMessageHandler)),
            Entry("Sample.Message", typeof(SecondMessageHandler)),
        ];

        var exception = Assert.Throws<CompetingHandlersException>(() => new HandlerRegistry<OutboxMessage>(entries));

        Assert.Equal("Sample.Message", exception.MessageTypeName);
        Assert.Contains(nameof(ExampleMessageHandler), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SecondMessageHandler), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Calling one module's generated method twice contributes its entries twice.</summary>
    [Fact]
    public void Accepts_TheSameHandlerContributedTwice()
    {
        HandlerEntry<OutboxMessage>[] entries =
        [
            Entry("Sample.Message", typeof(ExampleMessageHandler)),
            Entry("Sample.Message", typeof(ExampleMessageHandler)),
        ];

        var registry = new HandlerRegistry<OutboxMessage>(entries);

        Assert.True(registry.TryGetEntry("Sample.Message", out _));
    }

    [Fact]
    public void DoesNotFindAnEntry_ForAnUnclaimedMessageType()
    {
        var registry = new HandlerRegistry<OutboxMessage>([Entry("Sample.Message", typeof(ExampleMessageHandler))]);

        Assert.False(registry.TryGetEntry("Sample.Other", out _));
    }

    /// <summary>
    /// A handler of several message types is registered once as its concrete type, so the lifetime it
    /// declares governs the handler rather than each message type it handles.
    /// </summary>
    [Fact]
    public void AScopedHandlerOfSeveralMessageTypes_IsOneInstancePerScope()
    {
        var services = new ServiceCollection();
        services.AddUndergroundOutboxTestMessageHandlers();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var asNested = scope.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Envelope.Nested>>();
        var asWrapped = scope.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Wrapped<Envelope.Nested>>>();

        Assert.Same(asNested, asWrapped);
    }

    [Fact]
    public void AScopedHandler_IsADifferentInstanceInAnotherScope()
    {
        var services = new ServiceCollection();
        services.AddUndergroundOutboxTestMessageHandlers();

        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var one = first.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Envelope.Nested>>();
        var other = second.ServiceProvider.GetRequiredService<Underground.Outbox.IOutboxMessageHandler<Envelope.Nested>>();

        Assert.NotSame(one, other);
    }
}
