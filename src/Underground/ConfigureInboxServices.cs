using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Underground.Inbox;

namespace Underground;

public static class ConfigureInboxServices
{
    public static IServiceCollection AddInboxServices(this IServiceCollection services, string moduleName, Action<InboxServiceConfiguration> configuration)
    {
        var serviceConfig = new InboxServiceConfiguration();
        configuration.Invoke(serviceConfig);

        // register all assigned handlers
        services.TryAddEnumerable(serviceConfig.HandlersWithLifetime);

        IInbox inbox = new InMemoryInbox();
        services.AddKeyedSingleton(moduleName, new InboxProcessor(inbox, serviceConfig.Handlers, services.BuildServiceProvider()));

        return services;
    }
}