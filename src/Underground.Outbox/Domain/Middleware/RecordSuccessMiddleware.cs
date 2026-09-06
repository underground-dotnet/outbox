using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Records the message as completed once the rest of the pipeline reports that it was. With
/// <see cref="RecordFailureMiddleware{TEntity}"/>, every run of the pipeline ends in exactly one write recording
/// what became of the message.
/// </summary>
/// <remarks>
/// It stands aside for any <see cref="ProcessingAttempt"/> other than <see cref="ProcessingStatus.Succeeded"/>, because
/// a middleware below has already recorded that outcome. Where this middleware sits is on
/// <see cref="MessagePipelineFactory"/> with the rest of the order.
/// </remarks>
internal sealed class RecordSuccessMiddleware<TEntity>(MarkCompleted<TEntity> markCompleted) : IMessageMiddleware<TEntity> where TEntity : class, IMessage
{
    public async Task<ProcessingAttempt> ExecuteAsync(TEntity message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        var attempt = await next(cancellationToken).ConfigureAwait(false);

        if (attempt.Status != ProcessingStatus.Succeeded)
        {
            return attempt;
        }

        var stillOurs = await markCompleted.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

        // not a failure - the effect happened, the message is just no longer ours to mark. Reported
        // rather than swallowed because it is the one case of a certain double delivery.
        return stillOurs ? attempt : ProcessingAttempt.LeaseLost(failure: null);
    }
}
