using Microsoft.Extensions.DependencyInjection;

namespace Underground;

internal sealed record HandlerDescriptor(Type MessageHandler, ServiceLifetime Lifetime);

public class OutboxServiceConfiguration
{
    // public Type OutboxType { get; set; } = typeof(InMemoryOutbox);
    internal Dictionary<Type, HandlerDescriptor> Handlers = [];

    public OutboxServiceConfiguration AddHandler<TMessageHandlerType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        return AddHandler(typeof(TMessageHandlerType), serviceLifetime);
    }

    public OutboxServiceConfiguration AddHandler(Type messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        // var interfaceType = messageHandlerType.GetInterfaces().FirstOrDefault();
        var interfaceType = messageHandlerType.GetInterface("Underground.IMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            var messageType = interfaceType.GetGenericArguments()[0];
            // Console.WriteLine($"is: {interfaceType.AssemblyQualifiedName}");
            Console.WriteLine($"is: {messageType}");

            Handlers[messageType] = new HandlerDescriptor(messageHandlerType, serviceLifetime);
        }
        // if (messageHandlerType.IsGenericType)
        // {
        //     Type genericTypeDefinition = messageHandlerType.GetGenericTypeDefinition();

        //     Console.WriteLine("Generic Type Definition: " + genericTypeDefinition);
        //     Console.WriteLine("Is this a generic type definition? " + genericTypeDefinition.IsGenericTypeDefinition);
        //     Console.WriteLine("Is it a generic type? " + genericTypeDefinition.IsGenericType);

        //     // Get the type arguments
        //     Type[] typeArguments = messageHandlerType.GetGenericArguments();
        //     Console.WriteLine("Type Arguments:");
        //     foreach (var argument in typeArguments)
        //     {
        //         Console.WriteLine(argument);
        //     }
        // }
        return this;
    }
}
