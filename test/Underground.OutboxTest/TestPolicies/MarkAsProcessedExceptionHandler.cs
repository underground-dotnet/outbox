using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;

namespace Underground.OutboxTest.TestPolicies;

public class MarkAsProcessedExceptionHandler<TEntity>() : IMessageExceptionHandler<TEntity> where TEntity : class, IMessage
{
#pragma warning disable CA1051 // Do not declare visible instance fields
    public int CallCount = 0;
#pragma warning restore CA1051 // Do not declare visible instance fields

    public async Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        CallCount++;
        message.ProcessedAt = DateTime.UtcNow;
        dbContext.Set<TEntity>().Update(message);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
