using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Isolates a failed Handler's writes from the attempt bookkeeping that follows, so that the retry count
/// and the new visibility instant still commit together with the rollback.
/// </summary>
/// <remarks>
/// Only ever assembled into a chain that runs inside a transaction; a side that dispatches with nothing
/// open leaves this stage out rather than making it optional at runtime.
/// </remarks>
internal sealed class SavepointStage<TEntity>(IDbContext dbContext) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    public async Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        // the factory only places this stage on a side that holds a transaction across the dispatch
        var transaction = dbContext.Database.CurrentTransaction!;

        var savepointName = $"processing_message_{message.Id}";
        await transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

        try
        {
            var handled = await next(cancellationToken).ConfigureAwait(false);
            await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

            return handled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // a cancellation is left alone: the transaction is about to be disposed and rolled back whole,
            // and issuing another statement on a cancelled token would only fail again
            await transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

            throw;
        }
    }
}
