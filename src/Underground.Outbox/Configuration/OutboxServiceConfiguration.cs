using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

/// <summary>Outbox settings, and the place to configure a discovered handler.</summary>
public class OutboxServiceConfiguration : ServiceConfiguration<OutboxMessage>
{
    /// <summary>
    /// Configures the handler that discovery already registered for this message type. Registration is
    /// the source generator's; this attaches exception policies to one handler and message type pair.
    /// </summary>
    /// <typeparam name="TH">The handler.</typeparam>
    /// <typeparam name="TM">The message type it handles.</typeparam>
    public PolicyBuilder<OutboxMessage> ForHandler<TH, TM>() where TH : class, IOutboxMessageHandler<TM>
    {
        var registration = new HandlerRegistration<OutboxMessage>(typeof(TH), typeof(TM));
        Registrations.Add(registration);
        return new PolicyBuilder<OutboxMessage>(registration);
    }
}
