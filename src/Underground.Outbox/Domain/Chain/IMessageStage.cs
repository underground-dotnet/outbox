using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// One concern in the work done to a single message, wrapped around the rest of the chain. Stages are
/// ordered, and the order is a correctness property rather than a preference - which is why the chain is
/// assembled by <see cref="MessageChainFactory"/> and cannot be composed from outside.
/// </summary>
internal interface IMessageStage<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Runs this stage around <paramref name="next"/>.
    /// </summary>
    /// <param name="message">The claimed Head this run of the chain is about.</param>
    /// <param name="scope">
    /// The scope the message is handled in. It is passed along rather than injected because the Handler is
    /// resolved from it at dispatch time.
    /// </param>
    /// <param name="next">The rest of the chain.</param>
    /// <param name="cancellationToken">Cancellation for this stage.</param>
    /// <returns>
    /// What became of the message. A stage that reports anything other than
    /// <see cref="AttemptStatus.Handled"/> has already recorded that outcome itself;
    /// <see cref="RecordSuccessStage{TEntity}"/> reads this on the way back out and is what records a
    /// handled message as processed. Note that the <see cref="Attempt"/> only travels outwards: nothing
    /// accumulates onto it on the way in, so it is a return value rather than a parameter threaded through.
    /// </returns>
    Task<Attempt> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken);
}
