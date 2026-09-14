using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The message's <see cref="IMessage.Type"/> is written from the runtime type and read by the Handler
/// Registry. Nested and generic types are where the two spellings used to diverge, leaving a message
/// nobody could handle.
/// </summary>
public class MessageTypeNameTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task DispatchAsync(OutboxMessage message)
    {
        var services = new ServiceCollection();

        // the generated registration is the only thing that puts handlers and entries in the container
        services.AddUndergroundOutboxTestMessageHandlers();
        services.AddSingleton<HandlerRegistry<OutboxMessage>>();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var dispatcher = new MessageDispatcher<OutboxMessage>(provider.GetRequiredService<HandlerRegistry<OutboxMessage>>());

        await dispatcher.ExecuteAsync(scope, message, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ReachesTheHandler_WhenTheMessageTypeIsNested()
    {
        var message = new OutboxMessage(Guid.NewGuid(), CreatedAt, new Envelope.Nested(42));

        await DispatchAsync(message);

        Assert.Equal("Underground.OutboxTest.TestHandler.Envelope+Nested", message.Type);
        Assert.Contains(NestedMessageHandler.CalledWithNested, m => m.Id == 42);
    }

    [Fact]
    public async Task ExecuteAsync_ReachesTheHandler_WhenTheMessageTypeIsGeneric()
    {
        var message = new OutboxMessage(Guid.NewGuid(), CreatedAt, new Wrapped<Envelope.Nested>(new Envelope.Nested(7)));

        await DispatchAsync(message);

        Assert.StartsWith("Underground.OutboxTest.TestHandler.Wrapped`1[[", message.Type, StringComparison.Ordinal);
        Assert.Contains(NestedMessageHandler.CalledWithWrapped, m => m.Body.Id == 7);
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenNoHandlerIsRegisteredForTheType()
    {
        var message = new OutboxMessage(Guid.NewGuid(), CreatedAt, "Sample.Unknown", "{}");

        await Assert.ThrowsAsync<ParsingException>(() => DispatchAsync(message));
    }

    /// <summary>
    /// The registry is keyed on the runtime spelling, so an entry is found by the exact string the write
    /// side stored rather than by the compiler's spelling of the same type.
    /// </summary>
    [Fact]
    public void Registry_IsKeyedOnTheStoredSpelling_ForNestedTypes()
    {
        var services = new ServiceCollection();
        services.AddUndergroundOutboxTestMessageHandlers();
        services.AddSingleton<HandlerRegistry<OutboxMessage>>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry<OutboxMessage>>();

        Assert.True(registry.TryGetEntry("Underground.OutboxTest.TestHandler.Envelope+Nested", out var entry));
        Assert.Equal(typeof(NestedMessageHandler), entry.HandlerType);
    }
}
