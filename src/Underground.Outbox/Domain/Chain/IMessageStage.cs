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
    /// Whether the message was handled, and with it whether the caller should record it as processed. A
    /// stage that reports <c>false</c> has already recorded the failed attempt itself.
    /// </returns>
    Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken);
}
