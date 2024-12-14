using Microsoft.Extensions.DependencyInjection;

using Underground.Inbox;

namespace Underground;

public static class ConfigureInboxServices
{
    public static IServiceCollection AddInboxServices(this IServiceCollection services, string moduleName, Action<InboxServiceConfiguration> configuration)
    {
        var serviceConfig = new InboxServiceConfiguration();
        configuration.Invoke(serviceConfig);

        IInbox inbox = new InMemoryInbox();
        services.AddKeyedSingleton(moduleName, new InboxProcessor(inbox, serviceConfig.Handlers, services.BuildServiceProvider()));

        // services.AddMassTransit(x =>
        // {
        //
        // });
        //
        // services.AddMediator(cfg =>
        // {
        //     // cfg.AddInMemoryInboxOutbox();
        // });

        // services.AddMediatR(cfg =>
        // {
        //     // this module does not listen to any notifications. It only publishes messages.
        //     cfg.RegisterServicesFromAssembly(typeof(IOutbox).Assembly);
        // });

        // services.AddSingleton(typeof(IOutbox), serviceConfig.OutboxType);
        // services.AddSingleton<IOutboxProcessor, OutboxProcessor>();

        return services;
    }
}