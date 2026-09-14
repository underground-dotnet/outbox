using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

/// <summary>Inbox settings, and the place to configure a discovered handler.</summary>
public class InboxServiceConfiguration : ServiceConfiguration<InboxMessage>
{
    /// <summary>
    /// Configures the handler that discovery already registered for this message type. Registration is
    /// the source generator's; this attaches exception policies to one handler and message type pair.
    /// </summary>
    /// <typeparam name="TH">The handler.</typeparam>
    /// <typeparam name="TM">The message type it handles.</typeparam>
    public PolicyBuilder<InboxMessage> ForHandler<TH, TM>() where TH : class, IInboxMessageHandler<TM>
    {
        var registration = new HandlerRegistration<InboxMessage>(typeof(TH), typeof(TM));
        Registrations.Add(registration);
        return new PolicyBuilder<InboxMessage>(registration);
    }
}
