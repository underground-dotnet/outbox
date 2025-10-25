using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

public interface IMessageExceptionHandler
{
    public Task HandleAsync(MessageHandlerException ex, OutboxMessage message, IOutboxDbContext dbContext, CancellationToken cancellationToken);
}
