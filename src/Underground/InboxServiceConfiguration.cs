using Microsoft.Extensions.DependencyInjection;

namespace Underground;

public class InboxServiceConfiguration
{
    // public Type OutboxType { get; set; } = typeof(InMemoryOutbox);
    internal Dictionary<Type, HandlerDescriptor> Handlers = [];
    internal List<ServiceDescriptor> HandlersWithLifetime = [];

    public InboxServiceConfiguration AddHandler<TMessageHandlerType>()
    {
        return AddHandler(typeof(TMessageHandlerType));
    }

    public InboxServiceConfiguration AddHandler(Type messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        // var interfaceType = messageHandlerType.GetInterfaces().FirstOrDefault();
        var interfaceType = messageHandlerType.GetInterface("Underground.IMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            var messageType = interfaceType.GetGenericArguments()[0];
            // Console.WriteLine($"is: {interfaceType.AssemblyQualifiedName}");

            Handlers[messageType] = new HandlerDescriptor(messageHandlerType);
            HandlersWithLifetime.Add(new ServiceDescriptor(interfaceType, messageHandlerType, serviceLifetime));
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
