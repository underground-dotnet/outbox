namespace Underground.Outbox;

public interface IOutboxMessageHandler<in T>
{
    public IOutboxErrorHandler ErrorHandler { get; }

    public Task HandleAsync(T message, CancellationToken cancellationToken);
}

