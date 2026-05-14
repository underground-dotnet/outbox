using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.HandlerRegistrations;

internal sealed class HandlerRegistration<TEntity>(
    HandlerType handlerType,
    MessageType messageType,
    ServiceDescriptor serviceDescriptor
    ) : IPolicyStore<TEntity> where TEntity : class, IMessage
{
    internal HandlerType HandlerType { get; } = handlerType;
    internal MessageType MessageType { get; } = messageType;
    internal ServiceDescriptor ServiceDescriptor { get; } = serviceDescriptor;
    internal List<ExceptionPolicy<TEntity>> ExceptionPolicies { get; } = [];

    void IPolicyStore<TEntity>.AddExceptionPolicy(ExceptionPolicy<TEntity> policy)
    {
        ExceptionPolicies.Add(policy);
    }
}
