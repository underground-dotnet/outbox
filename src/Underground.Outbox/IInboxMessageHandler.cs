namespace Underground.Outbox;

public interface IInboxMessageHandler<in T>
{
    public Task HandleAsync(T message, CancellationToken cancellationToken);
}

