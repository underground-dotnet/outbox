using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// The end of the pipeline: hands the message to its Handler and persists whatever the Handler wrote.
/// </summary>
internal sealed class DispatchMessage<TEntity>(
    IMessageDispatcher<TEntity> dispatcher,
    IDbContext dbContext
) where TEntity : class, IMessage
{
    /// <summary>
    /// Invokes the Handler for this message. It returns only if the Handler did; the middleware wrapped around
    /// this call decide what a throw means. The save runs in the inbox's transaction, or - on the outbox,
    /// which holds none - in one of EF Core's own.
    /// </summary>
    internal async Task ExecuteAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
    {
        await dispatcher.ExecuteAsync(scope, message, cancellationToken).ConfigureAwait(false);

        // in case the handler forgot to call SaveChanges
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
