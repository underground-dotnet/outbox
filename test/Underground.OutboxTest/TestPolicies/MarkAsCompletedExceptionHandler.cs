using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.OutboxTest.TestPolicies;

public class MarkAsCompletedExceptionHandler<TEntity>() : IMessageExceptionHandler<TEntity> where TEntity : class, IMessage
{
#pragma warning disable CA1051 // Do not declare visible instance fields
    public int CallCount = 0;
#pragma warning restore CA1051 // Do not declare visible instance fields

    public Exception? ReceivedException { get; private set; }

    public async Task HandleAsync(Exception ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        CallCount++;
        ReceivedException = ex;
        message.CompletedAt = DateTime.UtcNow;
        dbContext.Set<TEntity>().Update(message);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
