using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Configuration;

public class InboxServiceConfiguration(ILogger<InboxServiceConfiguration> logger) : ServiceConfiguration
{
    public override ServiceConfiguration AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IInboxMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            logger.LogInformation("Added handler for {InterfaceType} with {MessageHandlerType}", interfaceType, messageHandlerType);
            HandlersWithLifetime.Add(new ServiceDescriptor(interfaceType, messageHandlerType, serviceLifetime));
        }

        return this;
    }
}
