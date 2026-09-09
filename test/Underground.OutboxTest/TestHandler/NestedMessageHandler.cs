using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

[OutboxHandler<TestDbContext>]
public class NestedMessageHandler : IOutboxMessageHandler<Envelope.Nested>, IOutboxMessageHandler<Wrapped<Envelope.Nested>>
{
    public static ConcurrentQueue<Envelope.Nested> CalledWithNested { get; } = new();
    public static ConcurrentQueue<Wrapped<Envelope.Nested>> CalledWithWrapped { get; } = new();

    public Task HandleAsync(Envelope.Nested message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithNested.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(Wrapped<Envelope.Nested> message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithWrapped.Enqueue(message);
        return Task.CompletedTask;
    }
}
