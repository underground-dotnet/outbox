using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class InboxServiceConfiguration : ServiceConfiguration
{
    public override HandlerRegistrationBuilder AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IInboxMessageHandler`1");
        if (interfaceType?.IsGenericType != true)
        {
            throw new ArgumentException($"{messageHandlerType} does not implement IInboxMessageHandler<T>.");
        }

        var registration = new HandlerRegistration(interfaceType, messageHandlerType, serviceLifetime);
        HandlerRegistrations.Add(registration);
        return new HandlerRegistrationBuilder(registration);
    }
}
