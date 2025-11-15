using Medallion.Threading;
using Medallion.Threading.Postgres;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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

        // register all assigned handlers
        services.TryAddEnumerable(serviceConfig.HandlersWithLifetime);

        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<AddMessageToOutbox>();
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IMessageDispatcher<OutboxMessage>, OutboxDispatcher>();
        services.AddScoped<IMessageExceptionHandler<OutboxMessage>, DiscardMessageOnExceptionHandler<OutboxMessage>>();
        services.AddScoped<ProcessExceptionFromHandler<OutboxMessage>>();
        services.AddSingleton(
            provider => new OutboxProcessor<OutboxMessage>(
                serviceConfig,
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IMessageDispatcher<OutboxMessage>>(),
                provider.GetRequiredService<ILogger<OutboxProcessor<OutboxMessage>>>()
            )
        );
        services.AddHostedService<OutboxBackgroundService>();

        var serviceProvider = services.BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<IOutboxDbContext>();
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Database connection string is not set. Please ensure the DbContext is properly configured.");
        }
        services.AddSingleton<IDistributedLockProvider>(_ => new PostgresDistributedSynchronizationProvider(connectionString));

        return services;
    }
}
