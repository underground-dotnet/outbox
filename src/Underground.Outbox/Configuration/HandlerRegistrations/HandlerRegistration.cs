using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.HandlerRegistrations;

/// <summary>
/// What configuration has to say about one discovered handler and message type pair. It does not put
/// the handler in the container - discovery does that - it only carries the policies attached to it.
/// </summary>
internal sealed class HandlerRegistration<TEntity>(
    HandlerType handlerType,
    MessageType messageType
    ) : IPolicyStore<TEntity> where TEntity : class, IMessage
{
    internal HandlerType HandlerType { get; } = handlerType;
    internal MessageType MessageType { get; } = messageType;
    internal List<ExceptionPolicy<TEntity>> ExceptionPolicies { get; } = [];

    void IPolicyStore<TEntity>.AddExceptionPolicy(ExceptionPolicy<TEntity> policy)
    {
        ExceptionPolicies.Add(policy);
    }
}
