using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class InboxServiceConfiguration : ServiceConfiguration
{
    public override ServiceConfiguration AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IInboxMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            Console.WriteLine($"Added handler for {interfaceType} with {messageHandlerType} ");
            HandlersWithLifetime.Add(new ServiceDescriptor(interfaceType, messageHandlerType, serviceLifetime));
        }

        return this;
    }
}
