using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

[OutboxHandler<TestDbContext>]
public class MultipleMessagesHandler : IOutboxMessageHandler<MultiMessageA>, IOutboxMessageHandler<MultiMessageB>
{
    public static ConcurrentQueue<MultiMessageA> CalledWithA { get; } = new();
    public static ConcurrentQueue<MultiMessageB> CalledWithB { get; } = new();

    public Task HandleAsync(MultiMessageA message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithA.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(MultiMessageB message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithB.Enqueue(message);
        return Task.CompletedTask;
    }
}

#pragma warning disable MA0048 // File name must match type name
public record MultiMessageA(int Id);
public record MultiMessageB(int Id);
#pragma warning restore MA0048 // File name must match type name
