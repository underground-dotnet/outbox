using Medallion.Threading;
using Medallion.Threading.Postgres;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Configuration;

public static class ConfigureOutboxServices
{
    public static IServiceCollection AddOutboxServices<TContext>(
        this IServiceCollection services,
        Action<OutboxServiceConfiguration> configuration
    ) where TContext : DbContext, IOutboxDbContext
    {
        var serviceConfig = new OutboxServiceConfiguration();
        configuration.Invoke(serviceConfig);

        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<AddMessageToOutbox>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IMessageDispatcher<OutboxMessage>, OutboxDispatcher>();

        AddGenericServices<OutboxMessage, IOutboxDbContext>(services, serviceConfig);

        return services;
    }

    public static IServiceCollection AddInboxServices<TContext>(
        this IServiceCollection services,
        Action<InboxServiceConfiguration> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        var serviceConfig = new InboxServiceConfiguration();
        configuration.Invoke(serviceConfig);

        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<AddMessageToInbox>();
        services.AddScoped<IInbox, Inbox>();
        services.AddScoped<IMessageDispatcher<InboxMessage>, InboxDispatcher>();

        AddGenericServices<InboxMessage, IInboxDbContext>(services, serviceConfig);

        return services;
    }

    private static void AddGenericServices<TEntity, TContext>(this IServiceCollection services, ServiceConfiguration serviceConfig)
    where TEntity : class, IMessage
    where TContext : IDbContext
    {
        services.AddSingleton(serviceConfig);

        // register all assigned handlers
        services.TryAddEnumerable(serviceConfig.HandlersWithLifetime);

        services.AddScoped<IMessageExceptionHandler<TEntity>, DiscardMessageOnExceptionHandler<TEntity>>();
        services.AddScoped<ProcessExceptionFromHandler<TEntity>>();
        services.AddSingleton<Processor<TEntity>>();
        services.AddHostedService<BackgroundService<TEntity>>();

        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            var dbContext = sp.GetRequiredService<TContext>();
            var connectionString = dbContext.Database.GetConnectionString();
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException("Database connection string is not set. Please ensure the DbContext is properly configured.");
            }
            return new PostgresDistributedSynchronizationProvider(connectionString);
        });
    }
}
