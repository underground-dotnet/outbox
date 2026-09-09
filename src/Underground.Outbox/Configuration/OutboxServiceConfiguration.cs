using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

/// <summary>
/// Settings for the outbox belonging to <typeparamref name="TContext"/>.
/// </summary>
/// <typeparam name="TContext">The context this outbox belongs to.</typeparam>
public class OutboxServiceConfiguration<TContext> : ServiceConfiguration<TContext, OutboxMessage>
    where TContext : DbContext, IOutboxDbContext
{
    /// <summary>
    /// Registers a Handler for one message type in this outbox. The Handler must also carry
    /// <c>[OutboxHandler&lt;TContext&gt;]</c>, which is what binds it to this context at compile time.
    /// </summary>
    public PolicyBuilder<OutboxMessage> AddHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TH, TM>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient) where TH : class, IOutboxMessageHandler<TM>
    {
        var registration = new HandlerRegistration<OutboxMessage>(
            typeof(TH),
            typeof(TM),
            // keyed on the context, so a neighbour handling the same message type cannot be resolved here
            new ServiceDescriptor(typeof(IOutboxMessageHandler<TM>), typeof(TContext), typeof(TH), serviceLifetime)
        );
        Registrations.Add(registration);
        return new PolicyBuilder<OutboxMessage>(registration);
    }
}
