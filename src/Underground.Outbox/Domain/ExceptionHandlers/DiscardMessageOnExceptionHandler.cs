using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal class DiscardMessageOnExceptionHandler<TEntity>() : IMessageExceptionHandler<TEntity> where TEntity : class, IMessage
{
    public async Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Set<TEntity>()
            .Where(m => m.Id == message.Id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
