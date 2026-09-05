using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// Turns a Handler that threw into a recorded attempt: pushes the message out of sight for its backoff
/// delay, then consults the exception policies. Reports the failure to the caller rather than
/// rethrowing, so that one bad message costs its own Group and nothing else.
/// </summary>
/// <remarks>
/// The retry is written before the policies run rather than after, because it is the guarded write: it
/// is what establishes that this worker still holds the message. An exception policy writes by id and
/// cannot be guarded - it is consumer code - so a lost Lease has to be discovered before one is allowed
/// to delete or complete a message some other worker now owns.
/// </remarks>
internal sealed partial class RecordFailureStage<TEntity>(
    IDbContext dbContext,
    ScheduleRetry<TEntity> scheduleRetry,
    ILogger<RecordFailureStage<TEntity>> logger
) : IMessageStage<TEntity> where TEntity : class, IMessage
{
    private readonly ILogger<RecordFailureStage<TEntity>> _logger = logger;

    public async Task<bool> ExecuteAsync(TEntity message, IServiceScope scope, HandleMessageStep next, CancellationToken cancellationToken)
    {
        // only an exception the Handler itself raised has a policy to consult; anything else falls
        // straight through to the retry
        MessageHandlerException? handlerException = null;

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (MessageHandlerException ex)
        {
            LogMessageHandlerError(message.Id, ex);
            handlerException = ex;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMessageProcessingError(message.Id, ex);
        }

        // the try block returns on success, so reaching here means the message failed

        // clear all tracked entities, because processing failed. The exception handler can then use the
        // clean context to perform db operations.
        dbContext.ChangeTracker.Clear();

        // records the attempt and moves the message out of sight for the backoff delay, so the next
        // run does not retry it immediately
        var stillOurs = await scheduleRetry.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

        if (stillOurs && handlerException is not null)
        {
            // resolved from the scope the message is handled in rather than from this stage's own, so that
            // an exception handler sees the same services the Handler that raised it saw
            var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

            await processHandlerException.ExecuteAsync(handlerException, message, dbContext, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Error processing message {MessageId} in handler")]
    private partial void LogMessageHandlerError(long messageId, Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Error processing message {MessageId}.")]
    private partial void LogMessageProcessingError(long messageId, Exception exception);
}
