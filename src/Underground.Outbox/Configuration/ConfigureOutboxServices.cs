using Medallion.Threading;
using Medallion.Threading.Postgres;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Dispatcher;
using Underground.Outbox.ErrorHandler;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Configuration;

public static class ConfigureOutboxServices
{
    public static IServiceCollection AddOutboxServices(this IServiceCollection services, Action<OutboxServiceConfiguration> configuration)
    {
        var serviceConfig = new OutboxServiceConfiguration();
        configuration.Invoke(serviceConfig);

        if (serviceConfig.CreateSchemaAutomatically)
        {
            var provider = services.BuildServiceProvider();
            RunMigrations(serviceConfig, provider);
        }

        // register all assigned handlers
        services.TryAddEnumerable(serviceConfig.HandlersWithLifetime);

        services.AddScoped(_ => new AddMessageToOutbox(serviceConfig));
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<OutboxReflectionErrorHandler>();
        services.AddScoped<IOutboxMessageDispatcher, DirectInvocationDispatcher>();
        services.AddScoped(
            provider => new OutboxProcessor(
                serviceConfig,
                provider.GetRequiredService<IMessageDispatcher>(),
                provider.GetRequiredService<OutboxReflectionErrorHandler>(),
                provider.GetRequiredService<ILogger<OutboxProcessor>>()
            )
        );
        services.AddHostedService(provider =>
            new OutboxBackgroundService(
                serviceConfig,
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IDistributedLockProvider>(),
                provider.GetRequiredService<ILogger<OutboxBackgroundService>>()
            )
        );

        var dbContext = GetDbContext(serviceConfig, services.BuildServiceProvider());
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Database connection string is not set. Please ensure the DbContext is properly configured.");
        }
        services.AddSingleton<IDistributedLockProvider>(_ => new PostgresDistributedSynchronizationProvider(connectionString));

        return services;
    }

    private static DbContext GetDbContext(OutboxServiceConfiguration config, IServiceProvider provider)
    {
        var dbContextType = config.DbContextType ?? throw new NoDbContextAssignedException();
        var dbContext = (DbContext)provider.GetRequiredService(dbContextType);
        return dbContext;
    }

    private static void RunMigrations(OutboxServiceConfiguration config, IServiceProvider provider)
    {
        using var dbContext = GetDbContext(config, provider);
        using var connection = dbContext.Database.GetDbConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {config.FullTableName} (
                ""id"" int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                ""trace_id"" UUID NOT NULL UNIQUE,
                ""occurred_on"" TIMESTAMPTZ NOT NULL,
                ""type"" text NOT NULL,
                ""headers"" JSONB,
                ""partition_key"" text NOT NULL,
                ""data"" text,
                ""payload"" BYTEA,
                ""completed"" BOOLEAN NOT NULL DEFAULT FALSE,
                ""executed_at"" TIMESTAMPTZ NULL,
                ""retry_count"" int NOT NULL DEFAULT 0
            );
        ";

        command.ExecuteNonQuery();
        connection.Close();
    }
}
