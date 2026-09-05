using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Blocks inside the handler for the message ids listed in <see cref="BlockingIds"/> and returns at once
/// for every other one, so a test can hold one Group open and watch what the rest of the system does
/// meanwhile. The waiting is on a signal rather than on a duration, so nothing here sleeps.
/// </summary>
public class BlockingMessageHandler : IOutboxMessageHandler<BlockingMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; set; } = new();

    /// <summary>Ids whose handler blocks until <see cref="Release"/> is completed.</summary>
    public static ISet<int> BlockingIds { get; set; } = new HashSet<int>();

    /// <summary>Completes once a blocking handler has actually been entered.</summary>
    public static TaskCompletionSource Blocked { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed by the test to let the blocking handlers return.</summary>
    public static TaskCompletionSource Release { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static void Reset()
    {
        CalledWith = new ConcurrentQueue<int>();
        BlockingIds = new HashSet<int>();
        Blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task HandleAsync(BlockingMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);

        if (!BlockingIds.Contains(message.Id))
        {
            return;
        }

        Blocked.TrySetResult();

        // the timeout only ever elapses when the test has already failed, and then it releases the worker
        // so that the host can shut down instead of hanging
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }
}
