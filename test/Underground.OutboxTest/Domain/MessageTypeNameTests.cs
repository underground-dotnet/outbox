using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The message's <see cref="IMessage.Type"/> is written from the runtime type and read by the generated
/// dispatcher. Nested and generic types are where the two spellings used to diverge, leaving a message
/// nobody could handle.
/// </summary>
public class MessageTypeNameTests
{
    private static readonly DateTime CreatedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task DispatchAsync(OutboxMessage message)
    {
        var services = new ServiceCollection();
        var handler = new NestedMessageHandler();
        services.AddSingleton<IOutboxMessageHandler<Envelope.Nested>>(handler);
        services.AddSingleton<IOutboxMessageHandler<Wrapped<Envelope.Nested>>>(handler);

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await new GeneratedDispatcher<OutboxMessage>().ExecuteAsync(scope, message, TestContext.Current.CancellationToken);
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
}
