using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Announces the claimed message before anything is done to it, so that a Handler which never returns is
/// still attributable to a message and a Group, and reports what became of it on the way back out.
/// </summary>
/// <remarks>
/// Outermost, so the outcome line is written after every other stage has had its say. An inbound line with
/// no outbound line is therefore not a gap in the logging but a signal in its own right: the application
/// went down mid-attempt, and nothing about that message was recorded.
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
            // the exception is attached rather than formatted into the message, so that a failure and a
            // discarded attempt read the same way whichever of the two stages below settled it
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
