using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Bounds how long a Handler may run by narrowing the token the rest of the chain sees, so a Handler that
/// never returns gives back its worker and any transaction it held. On the outbox it is also what keeps a
/// worker inside its Lease, which is this budget plus a margin.
/// </summary>
/// <remarks>
/// The cancellation becomes a <see cref="HandlerTimeoutException"/> rather than staying an
/// <see cref="OperationCanceledException"/>, which <see cref="SavepointStage{TEntity}"/> and
/// <see cref="RecordFailureStage{TEntity}"/> both step aside for. Here the message is an ordinary failed
/// attempt. Where this stage sits is on <see cref="MessageChainFactory"/> with the rest of the order.
/// </remarks>
internal sealed class TimeoutStage<TEntity>(ServiceConfiguration<TEntity> config) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    public async Task<Attempt> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(config.HandlerTimeout);

        try
        {
            return await next(deadline.Token).ConfigureAwait(false);
        }
        // a cancellation from outside is a shutdown and travels on as one; only the deadline's own
        // cancellation becomes a failed attempt
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new HandlerTimeoutException(message.Id, config.HandlerTimeout, ex);
        }
    }
}
