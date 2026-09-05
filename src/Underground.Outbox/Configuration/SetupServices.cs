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

        // the outbox dispatches with no transaction open, so its chain leaves the savepoint out and its
        // outer loop is the three-transaction one
        services.AddScoped(MessageChainFactory.CreateOutbox);
        services.AddScoped<IProcessor<OutboxMessage>, OutboxProcessor>();

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

        // one transaction spans the claim, the Handler and the outcome, so the inbox keeps the savepoint
        services.AddScoped<SavepointStage<InboxMessage>>();
        services.AddScoped(MessageChainFactory.CreateInbox);
        services.AddScoped<IProcessor<InboxMessage>, InboxProcessor>();

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
        services.AddScoped<MarkHandled<TEntity>>();

        // the per-message stages, shared by the inbox and the outbox. They are registered individually
        // but only ever composed by the factory, which owns the order between them - and which side gets
        // which of them.
        services.AddScoped<LogMessageStage<TEntity>>();
        services.AddScoped<RecordSuccessStage<TEntity>>();
        services.AddScoped<RecordFailureStage<TEntity>>();
        services.AddScoped<TimeoutStage<TEntity>>();
        services.AddScoped<DispatchMessage<TEntity>>();

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
