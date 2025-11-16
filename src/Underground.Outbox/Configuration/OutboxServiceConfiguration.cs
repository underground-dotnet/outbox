using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Configuration;

public class OutboxServiceConfiguration(ILogger<OutboxServiceConfiguration> logger) : ServiceConfiguration
{
    public override ServiceConfiguration AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IOutboxMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            logger.LogInformation("Added handler for {InterfaceType} with {MessageHandlerType}", interfaceType, messageHandlerType);
            HandlersWithLifetime.Add(new ServiceDescriptor(interfaceType, messageHandlerType, serviceLifetime));
        }

        return this;
    }
}
