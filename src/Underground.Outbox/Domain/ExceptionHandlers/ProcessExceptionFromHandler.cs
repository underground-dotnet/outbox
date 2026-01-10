using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal class ProcessExceptionFromHandler<TEntity>(IEnumerable<IMessageExceptionHandler<TEntity>> handlers) where TEntity : class, IMessage
{
    private readonly IEnumerable<IMessageExceptionHandler<TEntity>> _handlers = handlers;

    internal async Task ExecuteAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(ex, message, dbContext, cancellationToken);
        }
    }
}
