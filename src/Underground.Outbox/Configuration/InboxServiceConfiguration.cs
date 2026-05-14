using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

public class InboxServiceConfiguration : ServiceConfiguration<InboxMessage>
{
    public PolicyBuilder<InboxMessage> AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TH, TM>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient) where TH : class, IInboxMessageHandler<TM>
    {
        Console.WriteLine($"Added handler for {typeof(IInboxMessageHandler<TM>)} with {typeof(TH)} ");

        var registration = new HandlerRegistration<InboxMessage>(
            typeof(TH),
            typeof(TM),
            new ServiceDescriptor(typeof(IInboxMessageHandler<TM>), typeof(TH), serviceLifetime)
        );
        Registrations.Add(registration);
        return new PolicyBuilder<InboxMessage>(registration);
    }
}
