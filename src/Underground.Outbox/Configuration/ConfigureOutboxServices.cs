using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration;

/// <summary>
/// Provides extension methods for configuring and setting up Outbox and Inbox services in the dependency injection container.
/// </summary>
public static class ConfigureOutboxServices
{
    /// <summary>
    /// Configures and registers Outbox services in the dependency injection container.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements IOutboxDbContext.</typeparam>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configuration">An action to configure the Outbox service settings.</param>
    public static IServiceCollection AddOutboxServices<TContext>(
        this IServiceCollection services,
        Action<OutboxServiceConfiguration> configuration
    ) where TContext : DbContext, IOutboxDbContext
    {
        SetupServices.SetupInternalOutboxServices<TContext>(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers one Inbox: its own workers, settings and Handlers, running its transactions on
    /// <typeparamref name="TContext"/>. Call once per Inbox.
    /// </summary>
    /// <remarks>
    /// Bind Handlers to the Inbox by passing the same name to each assembly's generated
    /// <c>Add&lt;Assembly&gt;MessageHandlers(inbox)</c>.
    /// </remarks>
    /// <typeparam name="TContext">The context the Inbox's transactions and its Handlers' writes run on.</typeparam>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="inbox">The Inbox's name, stored on each of its messages. Unique within the host.</param>
    /// <param name="configuration">An action to configure the Inbox service settings.</param>
    public static IServiceCollection AddInboxServices<TContext>(
        this IServiceCollection services,
        string inbox,
        Action<InboxServiceConfiguration> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        SetupServices.SetupInternalInboxServices<TContext>(services, inbox, configuration);

        return services;
    }
}
