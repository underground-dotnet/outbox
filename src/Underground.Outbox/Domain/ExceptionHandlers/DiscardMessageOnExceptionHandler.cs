using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal class DiscardMessageOnExceptionHandler<TEntity> : IMessageExceptionHandler<TEntity> where TEntity : class, IMessage
{
    public async Task HandleAsync(MessageHandlerException ex, TEntity message, DbContext dbContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(message);

        await dbContext.Set<TEntity>()
            .Where(m => m.Id == message.Id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
