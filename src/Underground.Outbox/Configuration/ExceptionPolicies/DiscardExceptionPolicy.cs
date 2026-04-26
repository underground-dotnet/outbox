using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

internal sealed record DiscardExceptionPolicy<TEntity>(Type ExceptionType) : ExceptionPolicy<TEntity, DiscardMessageOnExceptionHandler<TEntity>>(ExceptionType) where TEntity : class, IMessage
{
    //     public override async Task HandleExceptionAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    //     {
    // #pragma warning disable CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled
    //         Logger.LogInformation(
    //             ex.InnerException,
    //             "Handler {HandlerType} has DiscardOnAttribute for {ExceptionType}. Discarding message {MessageId}",
    //             ex.HandlerType.Name,
    //             ex.InnerException,
    //             message.Id
    //         );
    // #pragma warning restore CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled


    //         await dbContext.Set<TEntity>()
    //             .Where(m => m.Id == message.Id)
    //             .ExecuteDeleteAsync(cancellationToken);
    //     }
}
