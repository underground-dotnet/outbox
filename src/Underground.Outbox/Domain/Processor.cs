using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Domain;

internal sealed partial class Processor<TEntity>(
    IMessageDispatcher<TEntity> dispatcher,
    IDbContext dbContext,
    ILogger<Processor<TEntity>> logger,
    ClaimHead<TEntity> claimHead,
    ScheduleRetry<TEntity> scheduleRetry
) where TEntity : class, IMessage
{
    private readonly IMessageDispatcher<TEntity> _dispatcher = dispatcher;
    private readonly ILogger<Processor<TEntity>> _logger = logger;

    /// <summary>
    /// Handles the Group's Head - its oldest settled unhandled message - using the given scope and the
    /// DbContext of this instance. A Group offers only its Head, and it offers nothing at all while that
    /// Head is not yet visible, so a message in backoff or scheduled for later holds back everything
    /// behind it in the same Group.
    /// </summary>
    /// <returns>
    /// Whether a message was handled successfully, and with it whether the Group may hold a further one.
    /// It is <c>false</c> when the Group offered nothing and when the message it offered failed.
    /// </returns>
    internal async Task<bool> ProcessHeadAsync(string groupKey, IServiceScope scope, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var message = await claimHead.ExecuteAsync(groupKey, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return false;
            }

            var messageId = message.Id;
            LogProcessingMessage(messageId, typeof(TEntity).ToString(), groupKey);

            var handled = await CallMessageHandlerAsync(message, scope, cancellationToken).ConfigureAwait(false);

            if (handled)
            {
                await dbContext.Set<TEntity>()
                    .Where(m => m.Id == messageId)
                    .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow), cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // remove tracked entities to avoid memory leaks
            dbContext.ChangeTracker.Clear();

            return handled;
        }
    }

    private async Task<bool> CallMessageHandlerAsync(TEntity message, IServiceScope scope, CancellationToken cancellationToken)
    {
        var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

        var transaction = dbContext.Database.CurrentTransaction!;

        // the savepoint isolates a failed handler's writes from the attempt bookkeeping that follows, so
        // that the retry count and the new visibility instant still commit together with the rollback
        var savepointName = $"processing_message_{message.Id}";
        await transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

        // only an exception the handler itself raised has a policy to consult; anything else falls
        // straight through to the retry
        MessageHandlerException? handlerException = null;

        try
        {
            await _dispatcher.ExecuteAsync(scope, message, cancellationToken).ConfigureAwait(false);
            // persist all changes from the handler. (in case the handler forgot to call SaveChanges)
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

            return true;
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

        // clear all tracked entities, because processing failed. The ErrorHandler can then use the clean context to perform db operations.
        dbContext.ChangeTracker.Clear();
        await transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

        if (handlerException is not null)
        {
            await processHandlerException.ExecuteAsync(handlerException, message, dbContext, cancellationToken).ConfigureAwait(false);
        }

        // records the attempt and moves the message out of sight for the backoff delay, so the next
        // run does not retry it immediately
        await scheduleRetry.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

        return false;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Processing message {MessageId} in {Type} for group '{GroupKey}'")]
    private partial void LogProcessingMessage(long messageId, string type, string groupKey);

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
