using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatchers;

internal class OutboxDispatcher : DirectInvocationDispatcher<OutboxMessage>
{
    protected override Type CreateGenericType(Type eventType)
    {
        return typeof(IOutboxMessageHandler<>).MakeGenericType(eventType);
    }
}
