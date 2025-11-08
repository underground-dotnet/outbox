using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Dispatchers;

internal sealed class DirectInvocationDispatcher : IMessageDispatcher
{
    private static readonly ConcurrentDictionary<MessageType, HandlerType> HandlerTypeDictionary = new();
    private static readonly ConcurrentDictionary<MessageType, MethodInfo?> HandleMethodDictionary = new();

    public async Task ExecuteAsync(IServiceScope scope, IMessage message, CancellationToken cancellationToken = default)
    {
        MessageType? type = MessageType.GetType(message.Type) ?? throw new ParsingException($"Cannot resolve type '{message.Type}' of message: {message.Id}");

        var fullEvent = JsonSerializer.Deserialize(message.Data, type) ?? throw new ParsingException($"Cannot parse event body '{message.Data} of message: {message.Id}");

        // construct concrete handler type based on the event type and cache it so that the type is only resolved once
        // idea from: https://www.youtube.com/watch?v=5yLIzis9Qr0 it also shows more improvements we could do here
        Type interfaceType = HandlerTypeDictionary.GetOrAdd(
            fullEvent.GetType(),
            eventType => typeof(IOutboxMessageHandler<>).MakeGenericType(eventType)
        );

        // TODO: will throw when handler not found...
        // Console.WriteLine($"Handler not found for type {type}; all handlers: {string.Join(", ", config.Handlers.Keys)}");
        var handlerObject = scope.ServiceProvider.GetRequiredService(interfaceType);

        // resolve and invoke `Handle` method
        // resolve the method info from the type and cache it so that the method is only resolved once
        MethodInfo? method = HandleMethodDictionary.GetOrAdd(
            interfaceType,
            type => type.GetMethod("HandleAsync")
        );

        if (method is null)
        {
            throw new ParsingException($"Cannot find 'HandleAsync' method in handler type: {interfaceType.Name}");
        }

        try
        {
            var task = (Task?)method.Invoke(handlerObject, [fullEvent, cancellationToken]);
            if (task is not null)
            {
                await task;
            }
        }
        catch (TargetInvocationException ex)
        {
            throw new MessageHandlerException(
                handlerObject.GetType(),
                method,
                $"Error processing message {message.Id} with handler {handlerObject.GetType().Name}",
                ex.InnerException ?? ex
            );
        }
    }
}