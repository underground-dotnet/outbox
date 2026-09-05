using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Chain;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Configuration;

public static class SetupServices
{
    public static void SetupInternalOutboxServices<TContext>(
        IServiceCollection services,
        Action<OutboxServiceConfiguration> configuration
    ) where TContext : DbContext, IOutboxDbContext
    {
        var serviceConfig = new OutboxServiceConfiguration();
        configuration.Invoke(serviceConfig);
        serviceConfig.Validate();

        services.AddScoped<IOutboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<AddMessagesToOutbox>();
        services.AddScoped<IOutbox, OutboxImpl>();
        services.AddScoped<ClaimHead<OutboxMessage>, ClaimOutboxHead>();

        AddGenericServices<OutboxMessage, IOutboxDbContext>(services, serviceConfig);
    }

    public static void SetupInternalInboxServices<TContext>(
        IServiceCollection services,
        Action<InboxServiceConfiguration> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        var serviceConfig = new InboxServiceConfiguration();
        configuration.Invoke(serviceConfig);
        serviceConfig.Validate();

        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<AddMessagesToInbox>();
        services.AddScoped<IInbox, InboxImpl>();
        services.AddScoped<ClaimHead<InboxMessage>, ClaimInboxHead>();

        AddGenericServices<InboxMessage, IInboxDbContext>(services, serviceConfig);
    }

#pragma warning disable S2326 // Unused type parameters should be removed
    private static void AddGenericServices<TEntity, TContext>(this IServiceCollection services, ServiceConfiguration<TEntity> serviceConfig)
#pragma warning restore S2326 // Unused type parameters should be removed
    where TEntity : class, IMessage
    where TContext : IDbContext
    {
        services.AddSingleton(serviceConfig);

        // register all assigned handlers
        services.TryAddEnumerable(serviceConfig.Registrations.Select(r => r.ServiceDescriptor));

        services.AddSingleton<ConcurrentProcessor<TEntity>>();
        // services.AddScoped<IMessageExceptionHandler<TEntity>, DiscardMessageOnExceptionHandler<TEntity>>();
        services.AddScoped<DiscardMessageOnExceptionHandler<TEntity>>();
        services.AddScoped<ProcessExceptionFromHandler<TEntity>>();
        services.AddScoped<ScheduleRetry<TEntity>>();

        // the per-message chain, shared by the inbox and the outbox. Its stages are registered
        // individually but only ever composed by the factory, which owns the order between them.
        services.AddScoped<LogMessageStage<TEntity>>();
        services.AddScoped<RecordFailureStage<TEntity>>();
        services.AddScoped<SavepointStage<TEntity>>();
        services.AddScoped<DispatchMessage<TEntity>>();
        services.AddScoped(MessageChainFactory.Create<TEntity>);

        services.AddScoped<Processor<TEntity>>();
        services.AddScoped<DeleteProcessedMessages<TEntity>>();
        services.AddHostedService<BackgroundService<TEntity>>();
        services.AddHostedService<CleanupBackgroundService<TEntity>>();
        services.TryAddScoped<ProcessMessagesOnSaveChangesInterceptor>();

        // services.AddSingleton<IDistributedLockProvider>(sp =>
        // {
        //     var dbContext = sp.GetRequiredService<TContext>();
        //     var connectionString = dbContext.Database.GetConnectionString();
        //     if (string.IsNullOrEmpty(connectionString))
        //     {
        //         throw new ArgumentException("Database connection string is not set. Please ensure the DbContext is properly configured.");
        //     }
        //     return new PostgresDistributedSynchronizationProvider(connectionString);
        // });
    }
}
