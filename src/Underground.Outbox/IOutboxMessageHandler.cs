namespace Underground.Outbox;

using Underground.Outbox.Data;

public interface IOutboxMessageHandler<in T>
{
    /// <summary>
    /// Handles an outbox message.
    /// </summary>
    /// <param name="message">The deserialized message content.</param>
    /// <param name="metadata">Metadata about the message including ID and group key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task HandleAsync(T message, MessageMetadata metadata, CancellationToken cancellationToken);
}

