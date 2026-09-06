using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// One concern in the work done to a single message, wrapped around the rest of the chain. The order
/// between stages is a correctness property, so only <see cref="MessageChainFactory"/> composes them.
/// </summary>
internal interface IMessageStage<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Runs this stage around <paramref name="next"/>.
    /// </summary>
    /// <param name="message">The claimed Head this run of the chain is about.</param>
    /// <param name="scope">The scope the message is handled in; the Handler is resolved from it.</param>
    /// <param name="next">The rest of the chain.</param>
    /// <param name="cancellationToken">Cancellation for this stage.</param>
    /// <returns>
    /// What became of the message. A stage reporting anything other than
    /// <see cref="AttemptStatus.Handled"/> has already recorded that outcome itself. The
    /// <see cref="Attempt"/> only travels outwards, which is why it is a return value.
    /// </returns>
    Task<Attempt> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken);
}
