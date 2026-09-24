using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Throws the cancellation an <c>HttpClient</c> raises when its own timeout elapses: an
/// <see cref="OperationCanceledException"/> while the token the Handler was given is still live.
/// </summary>
public class SelfCancellingMessageHandler : IOutboxMessageHandler<SelfCancellingMessage>, IInboxMessageHandler<SelfCancellingMessage>
{
    public static ConcurrentQueue<int> CalledWith { get; } = new();

    public Task HandleAsync(SelfCancellingMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message.Id);

        throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.");
    }
}
