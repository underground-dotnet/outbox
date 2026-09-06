using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Announces the claimed message before anything is done to it, so a Handler that never returns is still
/// attributable, and reports what became of it on the way back out.
/// </summary>
/// <remarks>
/// Outermost, so an inbound line with no outbound line is a signal in its own right: the application went
/// down mid-attempt and nothing about that message was recorded.
/// </remarks>
internal sealed partial class LogMessageStage<TEntity>(
    ILogger<LogMessageStage<TEntity>> logger
) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    private readonly ILogger<LogMessageStage<TEntity>> _logger = logger;

    public async Task<Attempt> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        LogProcessingMessage(message.Id, typeof(TEntity).ToString(), message.GroupKey);

        var attempt = await next(cancellationToken).ConfigureAwait(false);

        if (attempt.Status == AttemptStatus.Handled)
        {
            LogMessageHandled(message.Id);
        }
        else
        {
            // attached rather than formatted in, so both failure kinds read the same way
            LogMessageNotHandled(message.Id, attempt.Status.ToString(), attempt.Failure);
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
        Message = "Handled message {MessageId}")]
    private partial void LogMessageHandled(long messageId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Message {MessageId} was not handled: {Status}")]
    private partial void LogMessageNotHandled(long messageId, string status, Exception? exception);
}
