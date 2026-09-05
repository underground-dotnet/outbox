using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Bounds how long a Handler may run, by narrowing the token the rest of the chain sees. A Handler that
/// never returns therefore gives its worker back rather than occupying it for good - and gives back the
/// transaction it would otherwise have held open along with it. On the outbox it is also what keeps a
/// worker inside its Lease: the Lease is this budget plus a margin, so the token fires first.
/// </summary>
/// <remarks>
/// The cancellation is translated into a <see cref="HandlerTimeoutException"/> rather than left as an
/// <see cref="OperationCanceledException"/>, because <see cref="SavepointStage{TEntity}"/> and
/// <see cref="RecordFailureStage{TEntity}"/> both step aside for one. That is right for a shutdown and
/// wrong here, where the message is an ordinary failed attempt. Where this stage sits, and why, is on
/// <see cref="MessageChainFactory"/> with the rest of the order.
/// </remarks>
internal sealed class TimeoutStage<TEntity>(ServiceConfiguration<TEntity> config) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    public async Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(config.HandlerTimeout);

        try
        {
            return await next(deadline.Token).ConfigureAwait(false);
        }
        // a cancellation that came from outside is the application shutting down, and has to keep
        // travelling as one; only the deadline's own cancellation becomes a failed attempt
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new HandlerTimeoutException(message.Id, config.HandlerTimeout, ex);
        }
    }
}
