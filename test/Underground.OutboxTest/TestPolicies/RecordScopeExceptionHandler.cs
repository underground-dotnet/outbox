using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.TestPolicies;

/// <summary>
/// Records the <see cref="ScopeProbe"/> of the scope the exception policy was resolved from.
/// </summary>
public class RecordScopeExceptionHandler<TEntity>(ScopeProbe probe) : IMessageExceptionHandler<TEntity> where TEntity : class, IMessage
{
    public Task HandleAsync(MessageHandlerException ex, TEntity message, IDbContext dbContext, CancellationToken cancellationToken)
    {
        RecordedScopes.Policy.Enqueue(probe);
        return Task.CompletedTask;
    }
}
