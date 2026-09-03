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
    FetchMessages<TEntity> fetchMessages,
    ScheduleRetry<TEntity> scheduleRetry
) where TEntity : class, IMessage
{
    private readonly IMessageDispatcher<TEntity> _dispatcher = dispatcher;
    private readonly ILogger<Processor<TEntity>> _logger = logger;

    /// <summary>
    /// Processes a batch of messages for the given group using a new scope and DbContext.
    /// </summary>
    /// <param name="groupKey"></param>
    /// <param name="batchSize"></param>
    /// <param name="scope"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A boolean indicating whether the whole batch was processed successfully, so that the group may hold more messages. It is false when no messages were found and when processing one of them failed.</returns>
    internal async Task<bool> ProcessMessagesAsync(string groupKey, int batchSize, IServiceScope scope, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {

            var messages = await fetchMessages.ExecuteAsync(groupKey, batchSize, cancellationToken).ConfigureAwait(false);
            var numberOfMessages = messages.Count;
            if (numberOfMessages == 0)
            {
                return false;

            }
            LogProcessingMessages(numberOfMessages, typeof(TEntity).ToString(), groupKey);

            var successIds = await CallMessageHandlersAsync(messages, scope, cancellationToken).ConfigureAwait(false);

            // mark as processed
            await dbContext.Set<TEntity>()
                .Where(m => successIds.Contains(m.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow), cancellationToken: cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // remove tracked entities to avoid memory leaks
            dbContext.ChangeTracker.Clear();

            return messages.Count == successIds.Count;
        }
    }

    private async Task<List<long>> CallMessageHandlersAsync(IEnumerable<TEntity> messages, IServiceScope scope, CancellationToken cancellationToken)
    {
        var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

        var transaction = dbContext.Database.CurrentTransaction!;
        var successfulIds = new List<long>();

        foreach (var message in messages)
        {
            var savepointName = $"processing_message_{message.Id}";
            await transaction.CreateSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
            Exception? exception = null;

            try
            {
                await _dispatcher.ExecuteAsync(scope, message, cancellationToken).ConfigureAwait(false);
                // persist all changes from the handler. (in case the handler forgot to call SaveChanges)
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                successfulIds.Add(message.Id);
                await transaction.ReleaseSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);
            }
            catch (MessageHandlerException ex)
            {
                LogMessageHandlerError(message.Id, ex);
                exception = ex;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMessageProcessingError(message.Id, ex);
                exception = ex;
            }

            if (exception is not null)
            {
                // clear all tracked entities, because the batch processing failed. The ErrorHandler can then use the clean context to perform db operations.
                dbContext.ChangeTracker.Clear();
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken).ConfigureAwait(false);

                if (exception is MessageHandlerException ex)
                {
                    await processHandlerException.ExecuteAsync(ex, message, dbContext, cancellationToken).ConfigureAwait(false);
                }

                // records the attempt and moves the message out of sight for the backoff delay, so the next
                // run does not retry it immediately
                await scheduleRetry.ExecuteAsync(message, cancellationToken).ConfigureAwait(false);

                // Break out of the foreach loop (stop processing on first failure)
                break;
            }
        }

        return successfulIds;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Processing {Count} messages in {Type} for group '{GroupKey}'")]
    private partial void LogProcessingMessages(int count, string type, string groupKey);

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
