using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Announces the claimed message before anything is done to it, so a Handler that never returns is still
/// attributable, and reports what became of it on the way back out.
/// </summary>
internal sealed partial class LogMessageMiddleware<TEntity>(
    ILogger<LogMessageMiddleware<TEntity>> logger
) : IMessageMiddleware<TEntity> where TEntity : class, IMessage
{
    private readonly ILogger<LogMessageMiddleware<TEntity>> _logger = logger;

    public async Task<ProcessingAttempt> ExecuteAsync(TEntity message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        LogProcessingMessage(message.Id, typeof(TEntity).ToString(), message.GroupKey);

        var attempt = await next(cancellationToken).ConfigureAwait(false);

        if (attempt.Status == ProcessingStatus.Succeeded)
        {
            LogMessageCompleted(message.Id);
        }
        else
        {
            // attached rather than formatted in, so both failure kinds read the same way
            LogMessageNotCompleted(message.Id, attempt.Status.ToString(), attempt.Failure);
        }

        return attempt;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Processing message {MessageId} in {Type} for group '{GroupKey}'")]
    private partial void LogProcessingMessage(long messageId, string type, string groupKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Completed message {MessageId}")]
    private partial void LogMessageCompleted(long messageId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Message {MessageId} was not handled: {Status}")]
    private partial void LogMessageNotCompleted(long messageId, string status, Exception? exception);
}
