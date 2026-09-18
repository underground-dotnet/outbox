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
    /// Configures and registers Inbox services in the dependency injection container.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements IInboxDbContext.</typeparam>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="configuration">An action to configure the Inbox service settings.</param>
    public static IServiceCollection AddInboxServices<TContext>(
        this IServiceCollection services,
        Action<InboxServiceConfiguration> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        SetupServices.SetupInternalInboxServices<TContext>(services, configuration);

        return services;
    }
}
