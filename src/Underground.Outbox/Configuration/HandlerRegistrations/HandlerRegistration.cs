using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Configuration.HandlerRegistrations;

internal sealed class HandlerRegistration<TEntity>(
    HandlerType handlerType,
    ServiceDescriptor serviceDescriptor
    ) where TEntity : class, IMessage
{
    internal HandlerType HandlerType { get; } = handlerType;
    internal ServiceDescriptor ServiceDescriptor { get; } = serviceDescriptor;
    internal List<ExceptionPolicy<TEntity, IMessageExceptionHandler<TEntity>>> ExceptionPolicies { get; } = [];
}
