using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Everything done to one claimed message, in order: the stages wrap each other outermost-first and
/// <see cref="DispatchMessage{TEntity}"/> sits innermost. The inbox and the outbox differ by one stage,
/// assembled in <see cref="MessageChainFactory"/>.
/// </summary>
/// <remarks>
/// Deliberately not here: the transaction boundary and the claim - what it takes to *hold* a message.
/// Those differ between the sides, so a stage owning them would have to branch on which side it was
/// running on. They live in <see cref="IProcessor{TEntity}"/> instead, one implementation per side.
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
    /// Nothing is reported back: the outcome is settled inside the chain, and a worker that acted on the
    /// <see cref="Attempt"/> would retry a message the backoff has already pushed out of sight.
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

        // the Handler returned rather than threw, which is the whole of what being handled means
        return Attempt.Handled;
    }
}
