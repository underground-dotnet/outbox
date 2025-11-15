using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Dispatchers;

internal sealed class InboxDispatcher : DirectInvocationDispatcher<InboxMessage>
{
    protected override Type CreateGenericType(Type eventType)
    {
        return typeof(IInboxMessageHandler<>).MakeGenericType(eventType);
    }
}
