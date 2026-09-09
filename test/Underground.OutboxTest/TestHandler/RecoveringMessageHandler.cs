using System.Collections.Concurrent;
using System.Data;

using Underground.Outbox;
using Underground.Outbox.Attributes;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Fails for the message ids listed in <see cref="FailingIds"/> and succeeds for every other, so a test
/// can let a message recover between two runs. One handler records both the failing and the succeeding
/// messages, which is what makes the order they were handled in observable in a single queue.
/// </summary>
[OutboxHandler<TestDbContext>]
public class RecoveringMessageHandler : IOutboxMessageHandler<RecoveringMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    public static ISet<int> FailingIds { get; set; } = new HashSet<int>();

    public Task HandleAsync(RecoveringMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);

        return FailingIds.Contains(message.Id)
            ? throw new DataException($"Failed to handle message {message.Id}")
            : Task.CompletedTask;
    }
}
