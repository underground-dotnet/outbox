using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

internal class ProcessExceptionFromHandler(IEnumerable<IMessageExceptionHandler> handlers)
{
    private readonly IEnumerable<IMessageExceptionHandler> _handlers = handlers;

    internal async Task ExecuteAsync(MessageHandlerException ex, OutboxMessage message, IOutboxDbContext dbContext, CancellationToken cancellationToken)
    {
        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(ex, message, dbContext, cancellationToken);
        }
    }
}
