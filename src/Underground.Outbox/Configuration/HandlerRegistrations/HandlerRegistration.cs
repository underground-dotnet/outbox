using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.HandlerRegistrations;

internal sealed class HandlerRegistration<TEntity>(
    HandlerType handlerType,
    MessageType messageType,
    ServiceDescriptor serviceDescriptor
    ) where TEntity : class, IMessage
{
    internal HandlerType HandlerType { get; } = handlerType;
    internal MessageType MessageType { get; } = messageType;
    internal ServiceDescriptor ServiceDescriptor { get; } = serviceDescriptor;
    internal List<ExceptionPolicy<TEntity>> ExceptionPolicies { get; } = [];
}
