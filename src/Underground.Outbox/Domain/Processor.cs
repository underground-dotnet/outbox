using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Domain;

internal sealed class Processor<TEntity>(
    IMessageDispatcher<TEntity> dispatcher,
    IDbContext dbContext,
    ILogger<Processor<TEntity>> logger,
    FetchMessages<TEntity> fetchMessages
) where TEntity : class, IMessage
{
    private readonly IMessageDispatcher<TEntity> _dispatcher = dispatcher;
    private readonly ILogger<Processor<TEntity>> _logger = logger;

    /// <summary>
    /// Processes a batch of messages for the given partition using a new scope and DbContext.
    /// </summary>
    /// <param name="partition"></param>
    /// <param name="batchSize"></param>
    /// <param name="scope"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A boolean indicating if any messages were found or if the outbox is empty. It only returns true if all found messages were processed successfully.</returns>
    internal async Task<bool> ProcessMessagesAsync(string partition, int batchSize, IServiceScope scope, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await fetchMessages.ExecuteAsync(partition, batchSize, cancellationToken);
        var numberOfMessages = messages.Count;
        if (numberOfMessages == 0)
        {
            return false;

        }
#pragma warning disable CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled
        _logger.LogInformation("Processing {Count} messages in {Type} for partition '{Partition}'", numberOfMessages, typeof(TEntity), partition);
#pragma warning restore CA1873 // Evaluation of this argument may be expensive and unnecessary if logging is disabled

        var successIds = await CallMessageHandlersAsync(messages, scope, cancellationToken);

        // mark as processed
        await dbContext.Set<TEntity>()
            .Where(m => successIds.Contains(m.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // remove tracked entities to avoid memory leaks
        dbContext.ChangeTracker.Clear();

        return messages.Count > 0 && messages.Count == successIds.Count();
    }

    private async Task<IEnumerable<long>> CallMessageHandlersAsync(IEnumerable<TEntity> messages, IServiceScope scope, CancellationToken cancellationToken)
    {
        var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

        var transaction = dbContext.Database.CurrentTransaction!;
        var successfulIds = new List<long>();

        foreach (var message in messages)
        {
            var savepointName = $"processing_message_{message.Id}";
            await transaction.CreateSavepointAsync(savepointName, cancellationToken);
            Exception? exception = null;

            try
            {
                await _dispatcher.ExecuteAsync(scope, message, cancellationToken);
                // persist all changes from the handler. (in case the handler forgot to call SaveChanges)
                await dbContext.SaveChangesAsync(cancellationToken);
                successfulIds.Add(message.Id);
                await transaction.ReleaseSavepointAsync(savepointName, cancellationToken);
            }
            catch (MessageHandlerException ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId} in handler", message.Id);
                exception = ex;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing message {MessageId}.", message.Id);
                exception = ex;
            }

            if (exception is not null)
            {
                // clear all tracked entities, because the batch processing failed. The ErrorHandler can then use the clean context to perform db operations.
                dbContext.ChangeTracker.Clear();
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken);

                if (exception is MessageHandlerException ex)
                {
                    await processHandlerException.ExecuteAsync(ex, message, dbContext, cancellationToken);
                }

                // TODO: decide if max retry count is reached or if a retry makes sense
                // TODO: remove or move to processHandlerException
                await IncrementRetryCountAsync(dbContext, message);

                // Break out of the foreach loop (stop processing on first failure)
                break;
            }
        }

        return successfulIds;
    }

    private static async Task IncrementRetryCountAsync(IDbContext dbContext, IMessage message)
    {
        await dbContext.Set<TEntity>()
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(m => m.RetryCount, m => m.RetryCount + 1));
    }
}
