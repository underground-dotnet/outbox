namespace Underground;

public interface IMessageHandler<in T>
{
    public Task Handle(T message);
}
