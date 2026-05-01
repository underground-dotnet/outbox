using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

public class OutboxServiceConfiguration : ServiceConfiguration<OutboxMessage>
{
    public HandlerRegistrationBuilder<OutboxMessage> AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TH, TM>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient) where TH : class, IOutboxMessageHandler<TM>
    {
        Console.WriteLine($"Added handler for {typeof(IOutboxMessageHandler<TM>)} with {typeof(TH)} ");

        var registration = new HandlerRegistration<OutboxMessage>(
            typeof(TH),
            typeof(TM),
            new ServiceDescriptor(typeof(IOutboxMessageHandler<TM>), typeof(TH), serviceLifetime)
        );
        Registrations.Add(registration);
        return new HandlerRegistrationBuilder<OutboxMessage>(registration);
    }
}
