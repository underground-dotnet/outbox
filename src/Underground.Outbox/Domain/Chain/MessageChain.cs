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
/// What is deliberately *not* here: the transaction boundary, the claim, and the write that records the
/// outcome. Those differ between the two sides - an outbox worker holds no transaction while it dispatches
/// - and expressing them as stages would mean a context object carrying nullable transaction and Lease
/// fields that every stage had to branch on. They live in <see cref="IProcessor{TEntity}"/> instead, one
/// implementation per side.
/// </remarks>
internal sealed class MessageChain<TEntity>(
    IReadOnlyList<IMessageStage<TEntity>> stages,
    DispatchMessage<TEntity> dispatch
) where TEntity : class, IMessage
{
    /// <summary>
    /// Runs the chain for one claimed message.
    /// </summary>
    /// <returns>
    /// Whether the message was handled. <c>false</c> means a stage caught the failure and recorded the
    /// attempt, so the caller must not record the message as processed.
    /// </returns>
    internal Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
        => ExecuteFromAsync(0, message, scope, cancellationToken);

    private Task<bool> ExecuteFromAsync(int index, TEntity message, IServiceScope scope, CancellationToken cancellationToken)
        => index == stages.Count
            ? DispatchAsync(message, scope, cancellationToken)
            : stages[index].ExecuteAsync(
                message,
                scope,
                token => ExecuteFromAsync(index + 1, message, scope, token),
                cancellationToken);

    private async Task<bool> DispatchAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
    {
        await dispatch.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

        // reaching here means the Handler returned rather than threw, which is the whole of what being
        // handled means; every other answer is a stage catching something on the way back out
        return true;
    }
}
