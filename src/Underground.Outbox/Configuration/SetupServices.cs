using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.Inboxes;
using Underground.Outbox.Domain.Middleware;
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
        services.AddScoped(sp => new MessageDbContext<OutboxMessage>(sp.GetRequiredService<TContext>()));
        services.AddScoped<IOutbox, OutboxImpl>();
        services.AddScoped<ClaimHeadMessage<OutboxMessage>, ClaimOutboxHeadMessage>();

        // no transaction open during dispatch: no savepoint, and the three-transaction outer loop
        services.AddScoped<RenewLeaseMiddleware>();
        services.AddScoped(MessagePipelineFactory.CreateOutbox);
        services.AddScoped<IProcessor<OutboxMessage>, OutboxProcessor>();

        AddOutboxSingletons(services, serviceConfig);
        AddPipelineServices<OutboxMessage>(services);
    }

    public static void SetupInternalInboxServices<TContext>(
        IServiceCollection services,
        string inbox,
        Action<InboxServiceConfiguration> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inbox);

        if (services.Any(d => d.ImplementationInstance is InboxRegistration existing && string.Equals(existing.Name, inbox, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"The Inbox '{inbox}' is already registered.", nameof(inbox));
        }

        var serviceConfig = new InboxServiceConfiguration();
        configuration.Invoke(serviceConfig);
        serviceConfig.Validate();

        services.AddSingleton(new InboxRegistration(inbox, typeof(TContext), serviceConfig));

        services.AddSingleton<IHostedService>(sp =>
        {
            var running = sp.GetRequiredService<InboxDirectory>()[inbox];
            return new BackgroundService<InboxMessage>(running.Processor, running.Registry);
        });
        services.AddSingleton<IHostedService>(sp =>
        {
            var running = sp.GetRequiredService<InboxDirectory>()[inbox];
            return new CleanupBackgroundService<InboxMessage>(
                running.ScopeFactory,
                running.Configuration,
                sp.GetRequiredService<ILogger<CleanupBackgroundService<InboxMessage>>>());
        });

        AddSharedInboxServices(services);
    }

    /// <summary>
    /// The services every Inbox shares. Registered once however many Inboxes there are: each resolves what
    /// differs between Inboxes from the <see cref="InboxScope"/> its worker bound the scope to.
    /// </summary>
    private static void AddSharedInboxServices(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(InboxDirectory)))
        {
            return;
        }

        services.AddSingleton<InboxDirectory>();
        services.AddScoped<IInbox, InboxImpl>();
        services.AddScoped<InboxScope>();

        services.AddScoped(sp => sp.GetRequiredService<InboxScope>().Inbox.Configuration);
        services.AddScoped(sp => sp.GetRequiredService<InboxScope>().Inbox.Registry);
        services.AddScoped(sp => sp.GetRequiredService<InboxScope>().Inbox.Backoff);
        services.AddScoped(sp => sp.GetRequiredService<InboxScope>().Inbox.Policies);
        services.AddScoped(sp => new MessageDbContext<InboxMessage>(
            (IDbContext)sp.GetRequiredService(sp.GetRequiredService<InboxScope>().Inbox.ContextType)));
        services.AddScoped(sp => new DeleteCompletedMessages<InboxMessage>(
            sp.GetRequiredService<MessageDbContext<InboxMessage>>(),
            sp.GetRequiredService<ServiceConfiguration<InboxMessage>>(),
            sp.GetRequiredService<InboxScope>().Inbox.Name));

        services.AddScoped<ClaimHeadMessage<InboxMessage>, ClaimInboxHeadMessage>();

        // one transaction spans claim, Handler and outcome, so the inbox keeps the savepoint
        services.AddScoped<SavepointMiddleware<InboxMessage>>();
        services.AddScoped(MessagePipelineFactory.CreateInbox);
        services.AddScoped<IProcessor<InboxMessage>, InboxProcessor>();

        AddPipelineServices<InboxMessage>(services);
    }

    /// <summary>
    /// What the outbox holds once for the whole host: its settings, Handlers, retry and exception policies,
    /// workers and cleanup.
    /// </summary>
    private static void AddOutboxSingletons(IServiceCollection services, OutboxServiceConfiguration serviceConfig)
    {
        services.AddSingleton<ServiceConfiguration<OutboxMessage>>(serviceConfig);
        services.AddSingleton(new RetryBackoff<OutboxMessage>(serviceConfig.BackoffBase, serviceConfig.MaxBackoff, serviceConfig.BackoffJitter));
        services.AddSingleton(new ExceptionPolicyTable<OutboxMessage>(serviceConfig.Registrations, serviceConfig.GlobalPolicies.ExceptionPolicies));

        services.TryAddSingleton<HandlerRegistry<OutboxMessage>>();

        services.AddSingleton<ConcurrentProcessor<OutboxMessage>>();
        services.AddScoped<DeleteCompletedMessages<OutboxMessage>>();
        services.AddHostedService<BackgroundService<OutboxMessage>>();
        services.AddHostedService<CleanupBackgroundService<OutboxMessage>>();
    }

    /// <summary>The per-message middleware and exception handling both sides run.</summary>
    private static void AddPipelineServices<TEntity>(IServiceCollection services) where TEntity : class, IMessage
    {
        // services.AddScoped<IMessageExceptionHandler<TEntity>, DiscardMessageOnExceptionHandler<TEntity>>();
        services.AddScoped<DiscardMessageOnExceptionHandler<TEntity>>();
        services.AddScoped<ProcessExceptionFromHandler<TEntity>>();

        // per-message middleware, registered individually but only ever composed by the factory, which owns
        // the order between them
        services.AddScoped<TraceMessageMiddleware<TEntity>>();
        services.AddScoped<LogMessageMiddleware<TEntity>>();
        services.AddScoped<RecordSuccessMiddleware<TEntity>>();
        services.AddScoped<RecordFailureMiddleware<TEntity>>();
        services.AddScoped<TimeoutMiddleware<TEntity>>();
        services.AddScoped<DispatchMessage<TEntity>>();

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
