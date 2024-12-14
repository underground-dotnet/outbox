using System;
using System.Globalization;
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

            Console.WriteLine("publish: " + type);

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

            var genericInterfaceType = typeof(IMessageHandler<>);
            var interfaceType = genericInterfaceType.MakeGenericType(fullEvent.GetType());
            var method = interfaceType.GetMethod("Handle");
            Console.WriteLine(interfaceType);
            method!.Invoke(handlerObject, [fullEvent]);


            return true;
        });
    }
}
