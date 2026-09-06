using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// One concern in the work done to a single message, wrapped around the rest of the pipeline. The order
/// between middleware is a correctness property, so only <see cref="MessagePipelineFactory"/> composes them.
/// </summary>
internal interface IMessageMiddleware<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// Runs this middleware around <paramref name="next"/>.
    /// </summary>
    /// <param name="message">The claimed HeadMessage this run of the pipeline is about.</param>
    /// <param name="scope">The scope the message is handled in; the Handler is resolved from it.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="cancellationToken">Cancellation for this middleware.</param>
    /// <returns>
    /// What became of the message. A middleware reporting anything other than
    /// <see cref="ProcessingStatus.Succeeded"/> has already recorded that outcome itself. The
    /// <see cref="ProcessingAttempt"/> only travels outwards, which is why it is a return value.
    /// </returns>
    Task<ProcessingAttempt> ExecuteAsync(TEntity message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken);
}
