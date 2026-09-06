using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Records the message as handled once the rest of the chain reports that it was. With
/// <see cref="RecordFailureStage{TEntity}"/>, every run of the chain ends in exactly one write recording
/// what became of the message.
/// </summary>
/// <remarks>
/// It stands aside for any <see cref="Attempt"/> other than <see cref="AttemptStatus.Handled"/>, because
/// a stage below has already recorded that outcome. Where this stage sits is on
/// <see cref="MessageChainFactory"/> with the rest of the order.
/// </remarks>
internal sealed class RecordSuccessStage<TEntity>(MarkHandled<TEntity> markHandled) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    public async Task<Attempt> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        var attempt = await next(cancellationToken).ConfigureAwait(false);

        if (attempt.Status != AttemptStatus.Handled)
        {
            return attempt;
        }

        var stillOurs = await markHandled.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

        // not a failure - the effect happened, the message is just no longer ours to mark. Reported
        // rather than swallowed because it is the one case of a certain double delivery.
        return stillOurs ? attempt : Attempt.LeaseLost(failure: null);
    }
}
