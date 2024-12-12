using Microsoft.Extensions.DependencyInjection;

namespace Underground;

public static class ConfigureOutboxServices
{
    public static IServiceCollection AddOutboxServices(this IServiceCollection services, Action<OutboxServiceConfiguration> configuration)
    {
        var serviceConfig = new OutboxServiceConfiguration();
        configuration.Invoke(serviceConfig);

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