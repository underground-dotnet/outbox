namespace Underground.Outbox;

public interface IOutboxMessageHandler<in T>
{
    public Task HandleAsync(T message, CancellationToken cancellationToken);
}

