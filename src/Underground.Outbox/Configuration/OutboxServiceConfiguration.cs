using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class OutboxServiceConfiguration : ServiceConfiguration
{
    public ServiceConfiguration AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TH, TM>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient) where TH : class, IOutboxMessageHandler<TM>
    {
        Console.WriteLine($"Added handler for {typeof(IOutboxMessageHandler<TM>)} with {typeof(TH)} ");
        HandlersWithLifetime.Add(new ServiceDescriptor(typeof(IOutboxMessageHandler<TM>), typeof(TH), serviceLifetime));

        return this;
    }
}
