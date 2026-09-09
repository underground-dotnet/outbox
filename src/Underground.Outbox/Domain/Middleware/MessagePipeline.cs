using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Everything done to one claimed message, in order: the middleware wrap each other outermost-first and
/// <see cref="DispatchMessage{TContext, TEntity}"/> sits innermost. The inbox and the outbox differ by one middleware,
/// assembled in <see cref="MessagePipelineFactory"/>.
/// </summary>
/// <remarks>
/// Deliberately not here: the transaction boundary and the claim - what it takes to *hold* a message.
/// Those differ between the sides, so a middleware owning them would have to branch on which side it was
/// running on. They live in <see cref="IProcessor{TContext, TEntity}"/> instead, one implementation per side.
/// </remarks>
internal sealed class MessagePipeline<TContext, TEntity>(
    IReadOnlyList<IMessageMiddleware<TContext, TEntity>> middleware,
    DispatchMessage<TContext, TEntity> dispatch
) where TContext : DbContext
    where TEntity : class, IMessage
{
    /// <summary>
    /// Runs the pipeline for one claimed message, which ends in the write recording what became of it.
    /// </summary>
    /// <remarks>
    /// Nothing is reported back: the outcome is recorded inside the pipeline, and a worker that acted on the
    /// <see cref="ProcessingAttempt"/> would retry a message the backoff has already pushed out of sight.
    /// </remarks>
    internal Task ExecuteAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
        => ExecuteFromAsync(0, message, scope, cancellationToken);

    private Task<ProcessingAttempt> ExecuteFromAsync(int index, TEntity message, IServiceScope scope, CancellationToken cancellationToken)
        => index == middleware.Count
            ? DispatchAsync(message, scope, cancellationToken)
            : middleware[index].ExecuteAsync(
                message,
                scope,
                token => ExecuteFromAsync(index + 1, message, scope, token),
                cancellationToken);

    private async Task<ProcessingAttempt> DispatchAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
    {
        await dispatch.ExecuteAsync(message, scope, cancellationToken).ConfigureAwait(false);

        // the Handler returned rather than threw, which is the whole of what completing a message means
        return ProcessingAttempt.Succeeded;
    }
}
