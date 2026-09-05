using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// The end of the chain: hands the message to its Handler and persists whatever the Handler wrote. Every
/// other stage is arranged around this one call.
/// </summary>
internal sealed class DispatchMessage<TEntity>(
    IMessageDispatcher<TEntity> dispatcher,
    IDbContext dbContext
) where TEntity : class, IMessage
{
    /// <summary>
    /// Invokes the Handler for this message. It returns only if the Handler did; a Handler that fails
    /// throws, and the stages wrapped around this call are what decide what that means.
    /// </summary>
    /// <remarks>
    /// The save runs in whatever the side around it provides: the inbox's transaction, or - on the outbox,
    /// which holds none - a transaction of EF Core's own for that one call.
    /// </remarks>
    internal async Task ExecuteAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
    {
        await dispatcher.ExecuteAsync(scope, message, cancellationToken).ConfigureAwait(false);

        // persist all changes from the handler. (in case the handler forgot to call SaveChanges)
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
