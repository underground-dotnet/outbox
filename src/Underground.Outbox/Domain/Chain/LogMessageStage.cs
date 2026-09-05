using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Announces the claimed message before anything is done to it, so that a Handler which never returns is
/// still attributable to a message and a Group.
/// </summary>
internal sealed partial class LogMessageStage<TEntity>(
    ILogger<LogMessageStage<TEntity>> logger
) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    private readonly ILogger<LogMessageStage<TEntity>> _logger = logger;

    public Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        LogProcessingMessage(message.Id, typeof(TEntity).ToString(), message.GroupKey);

        return next(cancellationToken);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Processing message {MessageId} in {Type} for group '{GroupKey}'")]
    private partial void LogProcessingMessage(long messageId, string type, string groupKey);
}
