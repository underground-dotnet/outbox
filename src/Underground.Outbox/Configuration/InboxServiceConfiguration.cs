using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class InboxServiceConfiguration : ServiceConfiguration
{
    public ServiceConfiguration AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TH, TM>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient) where TH : class, IInboxMessageHandler<TM>
    {
        Console.WriteLine($"Added handler for {typeof(IInboxMessageHandler<TM>)} with {typeof(TH)} ");
        HandlersWithLifetime.Add(new ServiceDescriptor(typeof(IInboxMessageHandler<TM>), typeof(TH), serviceLifetime));

        return this;
    }
}
