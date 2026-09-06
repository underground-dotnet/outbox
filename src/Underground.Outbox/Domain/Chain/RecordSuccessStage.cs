using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Records the message as handled once the rest of the chain reports that it was. This is the success
/// counterpart to <see cref="RecordFailureStage{TEntity}"/>: between them, every run of the chain ends in
/// exactly one write recording what became of the message.
/// </summary>
/// <remarks>
/// Where this stage sits, and why, is on <see cref="MessageChainFactory"/> with the rest of the order. It
/// stands aside for any <see cref="Attempt"/> other than <see cref="AttemptStatus.Handled"/>, because a
/// stage below has already recorded that outcome - which is why <see cref="MessageChain{TEntity}"/> reports
/// nothing to its caller.
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

        // a lost Lease here is not a failure: the effect really did happen, and the message is simply no
        // longer ours to mark. It is reported rather than swallowed because it is the one case in which an
        // effect has certainly been carried out twice - some other worker owns the message and will dispatch
        // it again - and that is what an operator wants to see the rate of.
        return stillOurs ? attempt : Attempt.LeaseLost(failure: null);
    }
}
