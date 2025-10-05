using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class OutboxServiceConfiguration
{
    public int BatchSize { get; set; } = 5;

    public int ParallelProcessingOfPartitions { get; set; } = 4;

    internal List<ServiceDescriptor> HandlersWithLifetime = [];

    public OutboxServiceConfiguration AddHandler<TMessageHandlerType>()
    {
        return AddHandler(typeof(TMessageHandlerType));
    }

    public OutboxServiceConfiguration AddHandler<TMessageHandlerType>(ServiceLifetime serviceLifetime)
    {
        return AddHandler(typeof(TMessageHandlerType), serviceLifetime);
    }

    public OutboxServiceConfiguration AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IOutboxMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            Console.WriteLine($"Added handler for {interfaceType} with {messageHandlerType} ");
            HandlersWithLifetime.Add(new ServiceDescriptor(interfaceType, messageHandlerType, serviceLifetime));
        }

        return this;
    }
}
