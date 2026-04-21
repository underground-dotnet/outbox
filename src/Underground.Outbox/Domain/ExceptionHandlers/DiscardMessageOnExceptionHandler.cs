using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

public class DiscardMessageOnExceptionHandler<TEntity>(
    ILogger<DiscardMessageOnExceptionHandler<TEntity>> logger,
    IDiscardOnExceptionMapping discardOnExceptionMapping) : IMessageExceptionHandler<TEntity>
    where TEntity : class, IMessage
{
    public async Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        var discardOnTypes = discardOnExceptionMapping.GetDiscardOnTypes(ex.HandlerType);
        if (discardOnTypes is null || ex.InnerException is null)
        {
            return;
        }

        if (discardOnTypes.Any(et => et.IsInstanceOfType(ex.InnerException)))
        {
#pragma warning disable CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled
            logger.LogInformation(
                ex.InnerException,
                "Handler {HandlerType} has discard mapping for {ExceptionType}. Discarding message {MessageId}",
                ex.HandlerType.Name,
                ex.InnerException.GetType(),
                message.Id
            );
#pragma warning restore CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled

            await dbContext.Set<TEntity>()
                .Where(m => m.Id == message.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
