using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

internal sealed class HandlerRegistration(
    Type serviceType,
    Type implementationType,
    ServiceLifetime serviceLifetime)
{
    public Type ServiceType { get; } = serviceType;
    public Type ImplementationType { get; } = implementationType;
    public ServiceLifetime ServiceLifetime { get; } = serviceLifetime;
    public List<HandlerExceptionPolicy> ExceptionPolicies { get; } = [];

    public ServiceDescriptor ToServiceDescriptor() => new(ServiceType, ImplementationType, ServiceLifetime);
}
