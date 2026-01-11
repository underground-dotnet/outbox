namespace Underground.Outbox;

public interface IOutboxMessageHandler<in T>
{
    // TODO: add metadata
    public Task HandleAsync(T message, CancellationToken cancellationToken);
}

