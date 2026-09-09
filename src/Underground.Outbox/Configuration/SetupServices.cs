using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Domain.Middleware;

namespace Underground.Outbox.Configuration;

/// <summary>
/// Registers everything one inbox or one outbox needs. Called by the generated per-context entry points,
/// which also register that context's dispatcher.
/// </summary>
public static class SetupServices
{
    /// <summary>
    /// Registers the outbox belonging to <typeparamref name="TContext"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The configuration names no schema.</exception>
    /// <exception cref="InvalidOperationException">Another context already claimed that schema.</exception>
    public static void SetupInternalOutboxServices<TContext>(
        IServiceCollection services,
        Action<OutboxServiceConfiguration<TContext>> configuration
    ) where TContext : DbContext, IOutboxDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var serviceConfig = new OutboxServiceConfiguration<TContext>();
        configuration.Invoke(serviceConfig);
        serviceConfig.Validate();
        SchemaRegistry.For(services).Claim(serviceConfig.Schema!, typeof(TContext));

        services.AddScoped<AddMessagesToOutbox<TContext>>();
        services.AddScoped<IOutbox<TContext>, OutboxImpl<TContext>>();
        services.AddScoped<ClaimHeadMessage<TContext, OutboxMessage>, ClaimOutboxHeadMessage<TContext>>();

        // no transaction open during dispatch: no savepoint, and the three-transaction outer loop
        services.AddScoped(MessagePipelineFactory.CreateOutbox<TContext>);
        services.AddScoped<IProcessor<TContext, OutboxMessage>, OutboxProcessor<TContext>>();

        AddGenericServices<TContext, OutboxMessage>(services, serviceConfig);
    }

    /// <summary>
    /// Registers the inbox belonging to <typeparamref name="TContext"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The configuration names no schema.</exception>
    /// <exception cref="InvalidOperationException">Another context already claimed that schema.</exception>
    public static void SetupInternalInboxServices<TContext>(
        IServiceCollection services,
        Action<InboxServiceConfiguration<TContext>> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var serviceConfig = new InboxServiceConfiguration<TContext>();
        configuration.Invoke(serviceConfig);
        serviceConfig.Validate();
        SchemaRegistry.For(services).Claim(serviceConfig.Schema!, typeof(TContext));

        services.AddScoped<AddMessagesToInbox<TContext>>();
        services.AddScoped<IInbox<TContext>, InboxImpl<TContext>>();
        services.AddScoped<ClaimHeadMessage<TContext, InboxMessage>, ClaimInboxHeadMessage<TContext>>();

        // one transaction spans claim, Handler and outcome, so the inbox keeps the savepoint
        services.AddScoped<SavepointMiddleware<TContext, InboxMessage>>();
        services.AddScoped(MessagePipelineFactory.CreateInbox<TContext>);
        services.AddScoped<IProcessor<TContext, InboxMessage>, InboxProcessor<TContext>>();

        AddGenericServices<TContext, InboxMessage>(services, serviceConfig);
    }

    private static void AddGenericServices<TContext, TEntity>(IServiceCollection services, ServiceConfiguration<TContext, TEntity> serviceConfig)
        where TContext : DbContext
        where TEntity : class, IMessage
    {
        services.AddSingleton(serviceConfig);

        foreach (var registration in serviceConfig.Registrations)
        {
            services.TryAdd(registration.ServiceDescriptor);
        }

        services.AddSingleton<ConcurrentProcessor<TContext, TEntity>>();
        services.AddSingleton<IWorkerSet>(sp => sp.GetRequiredService<ConcurrentProcessor<TContext, TEntity>>());
        services.AddSingleton<IWorkSignal<TContext, TEntity>>(sp => sp.GetRequiredService<ConcurrentProcessor<TContext, TEntity>>());
        services.AddSingleton<IRetentionSweep, RetentionSweep<TContext, TEntity>>();

        services.TryAddScoped<DiscardMessageOnExceptionHandler<TEntity>>();
        services.AddScoped<ProcessExceptionFromHandler<TContext, TEntity>>();
        services.AddScoped<ScheduleRetry<TContext, TEntity>>();
        services.AddScoped<MarkCompleted<TContext, TEntity>>();

        // per-message middleware, registered individually but only ever composed by the factory, which owns
        // the order between them
        services.AddScoped<TraceMessageMiddleware<TContext, TEntity>>();
        services.AddScoped<LogMessageMiddleware<TContext, TEntity>>();
        services.AddScoped<RecordSuccessMiddleware<TContext, TEntity>>();
        services.AddScoped<RecordFailureMiddleware<TContext, TEntity>>();
        services.AddScoped<TimeoutMiddleware<TContext, TEntity>>();
        services.AddScoped<DispatchMessage<TContext, TEntity>>();

        services.AddScoped<DeleteCompletedMessages<TContext, TEntity>>();
        services.TryAddScoped<ProcessMessagesOnSaveChangesInterceptor<TContext>>();

        // one hosted service for every registered inbox and outbox, and one retention loop covering all of
        // them: TryAddEnumerable dedupes on implementation type, so the second registration adds nothing
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MessageProcessingBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CleanupBackgroundService>());
    }
}
