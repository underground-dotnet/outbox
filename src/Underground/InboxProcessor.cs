using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

namespace Underground;

public class InboxProcessor(IInbox inbox, Dictionary<Type, HandlerDescriptor> Handlers, IServiceProvider services)
{
    public async Task ProcessAsync()
    {
        await inbox.GetNextMessageAsync(async msg =>
        {
            Type? type = Type.GetType(msg.Type);
            if (type is null)
            {
                // TODO: error handling
                return false;
            }

            var fullEvent = JsonSerializer.Deserialize(msg.Data, type);

            if (fullEvent is null)
            {
                return false;
            }

            // TODO: Error handling
            // TODO: wrap events in Feature container to prevent messages being sent to other modules!!!
            // TODO: cast breaks things
            // var container = new NotificationContainer<INotification>((INotification)fullEvent);
            // Console.WriteLine("event : " + container);
            // await mediator.Publish(container);

            var handler = Handlers.GetValueOrDefault(type);

            if (handler is null)
            {
                // TODO: error handling
                return false;
            }

            var handlerObject = services.GetRequiredService(handler.MessageHandler);

            // convert handlerObject from type `object` to type `IMessageHandler`
            var genericInterfaceType = typeof(IMessageHandler<>);
            var interfaceType = genericInterfaceType.MakeGenericType(fullEvent.GetType());

            // invoke `Handle` method
            var method = interfaceType.GetMethod("Handle");

            if (method is null)
            {
                // TODO:
                return false;
            }
            var task = (Task?)method.Invoke(handlerObject, [fullEvent]);
            if (task is not null)
            {
                await task;
            }

            return true;
        });
    }
}
