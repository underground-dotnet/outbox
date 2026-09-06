using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Isolates a failed Handler's writes from the attempt bookkeeping that follows, so that the retry count
/// and the new visibility instant still commit together with the rollback.
/// </summary>
/// <remarks>
/// Only ever assembled into a pipeline that runs inside a transaction - today the inbox alone - which is why
/// <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/> is read without a null check.
/// </remarks>
internal sealed class SavepointMiddleware<TEntity>(IDbContext dbContext) : IMessageMiddleware<TEntity> where TEntity : class, IMessage
{
    public async Task<ProcessingAttempt> ExecuteAsync(TEntity message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction!;

        var savepointName = $"processing_message_{message.Id}";
        await transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

        try
        {
            var attempt = await next(cancellationToken).ConfigureAwait(false);
            await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

            return attempt;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a cancellation is left alone: the transaction is about to be rolled back whole
            await transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

            throw;
        }
    }
}
