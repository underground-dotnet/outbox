using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class OutboxServiceConfiguration : ServiceConfiguration
{
    public override HandlerRegistrationBuilder AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IOutboxMessageHandler`1");
        if (interfaceType?.IsGenericType != true)
        {
            throw new ArgumentException($"{messageHandlerType} does not implement IOutboxMessageHandler<T>.");
        }

        var registration = new HandlerRegistration(interfaceType, messageHandlerType, serviceLifetime);
        HandlerRegistrations.Add(registration);
        return new HandlerRegistrationBuilder(registration);
    }
}
