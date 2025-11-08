using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.ExceptionHandlers;

public interface IMessageExceptionHandler<in TEntity> where TEntity : class, IMessage
{
    public Task HandleAsync(MessageHandlerException ex, TEntity message, IOutboxDbContext dbContext, CancellationToken cancellationToken);
}
