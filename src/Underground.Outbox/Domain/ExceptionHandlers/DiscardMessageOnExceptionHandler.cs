using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Attributes;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

public class DiscardMessageOnExceptionHandler(ILogger<DiscardMessageOnExceptionHandler> logger) : IMessageExceptionHandler
{
    public async Task HandleAsync(MessageHandlerException ex, OutboxMessage message, IOutboxDbContext dbContext, CancellationToken cancellationToken)
    {
        // TODO: can we make it more type safe? GetMethod(nameof(IOutboxMessageHandler<Message>.HandleAsync))
        // TODO: lookup should be cached
        var methodInfo = ex.HandlerType.GetMethod("HandleAsync");
        var attribute = methodInfo?.GetCustomAttribute<DiscardOnAttribute>();
        if (attribute != null && attribute.ExceptionTypes.Any(et => et.IsInstanceOfType(ex.InnerException)))
        {
            logger.LogInformation(
                ex.InnerException,
                "Handler {HandlerType} has DiscardOnAttribute for {ExceptionType}. Discarding message {MessageId}",
                ex.HandlerType.Name,
                ex.InnerException,
                message.Id
            );

            await dbContext.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
