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

        return services;
    }
}