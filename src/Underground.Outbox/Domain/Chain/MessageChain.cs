using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Everything done to one claimed message, in order: the stages wrap each other outermost-first and
/// <see cref="DispatchMessage{TEntity}"/> sits innermost. The inbox and the outbox run near-identical
/// chains - they differ by one stage, assembled in <see cref="MessageChainFactory"/> - so a change to any
/// of these concerns cannot be applied to one side and forgotten on the other.
/// </summary>
/// <remarks>
/// What is deliberately *not* here: the transaction boundary and the claim - what it takes to *hold* a
/// message, as opposed to what is done to one once held. Those differ between the two sides - an outbox
/// worker holds no transaction while it dispatches - so a stage that owned them would have to ask which
/// side it was running on and branch, which is the one thing the shared chain exists to make impossible.
/// The <see cref="Attempt"/> the stages pass back out is no help here either: it travels outwards only,
/// and a transaction is something the innermost stages would need handed to them on the way in. They live
/// in <see cref="IProcessor{TEntity}"/> instead, one implementation per side.
/// </remarks>
internal sealed class MessageChain<TEntity>(
    IReadOnlyList<IMessageStage<TEntity>> stages,
    DispatchMessage<TEntity> dispatch
) where TEntity : class, IMessage
{
    /// <summary>
    /// Runs the chain for one claimed message, which ends in the write recording what became of it.
    /// </summary>
    /// <remarks>
    /// Nothing is reported back. What became of the message is settled inside the chain - by
    /// <see cref="RecordSuccessStage{TEntity}"/> or <see cref="RecordFailureStage{TEntity}"/> - and a
    /// caller has no decision left to make on it. The <see cref="Attempt"/> deliberately stops here: a
    /// worker that acted on it would retry a message the backoff has already pushed out of sight.
    /// </remarks>
    internal Task ExecuteAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
        => ExecuteFromAsync(0, message, scope, cancellationToken);

    private Task<Attempt> ExecuteFromAsync(int index, TEntity message, IServiceScope scope, CancellationToken cancellationToken)
        => index == stages.Count
            ? DispatchAsync(message, scope, cancellationToken)
            : stages[index].ExecuteAsync(
                message,
                scope,
                token => ExecuteFromAsync(index + 1, message, scope, token),
                cancellationToken);

    private async Task<Attempt> DispatchAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
    {
        await dispatch.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

        // reaching here means the Handler returned rather than threw, which is the whole of what being
        // handled means; every other answer is a stage catching something on the way back out
        return Attempt.Handled;
    }
}
