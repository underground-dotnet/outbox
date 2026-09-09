using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

/// <summary>
/// Settings for the inbox belonging to <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The context this inbox belongs to.</typeparam>
public class InboxServiceConfiguration<TContext> : ServiceConfiguration<TContext, InboxMessage>
    where TContext : DbContext, IInboxDbContext
{
    /// <summary>
    /// Registers a Handler for one message type in this inbox. The Handler must also carry
    /// <c>[InboxHandler&lt;TContext&gt;]</c>, which is what binds it to this context at compile time.
    /// </summary>
    public PolicyBuilder<InboxMessage> AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TH, TM>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient) where TH : class, IInboxMessageHandler<TM>
    {
        var registration = new HandlerRegistration<InboxMessage>(
            typeof(TH),
            typeof(TM),
            // keyed on the context, so a neighbour handling the same message type cannot be resolved here
            new ServiceDescriptor(typeof(IInboxMessageHandler<TM>), typeof(TContext), typeof(TH), serviceLifetime)
        );
        Registrations.Add(registration);
        return new PolicyBuilder<InboxMessage>(registration);
    }
}
