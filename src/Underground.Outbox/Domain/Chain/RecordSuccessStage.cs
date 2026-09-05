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
/// consumes the <c>bool</c> the stages pass back out, which is the only thing that reads it - which is why
/// <see cref="MessageChain{TEntity}"/> reports nothing to its caller.
/// </remarks>
internal sealed class RecordSuccessStage<TEntity>(MarkHandled<TEntity> markHandled) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    public async Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        var handled = await next(cancellationToken).ConfigureAwait(false);

        if (handled)
        {
            // a lost Lease here is a warning rather than a failure: the effect really did happen, and the
            // message is simply no longer ours to mark
            await markHandled.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);
        }

        return handled;
    }
}
