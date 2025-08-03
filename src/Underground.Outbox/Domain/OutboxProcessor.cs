using Microsoft.EntityFrameworkCore;
using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;
using Underground.Outbox.ErrorHandler;
using Underground.Outbox.Domain.Dispatcher;
using Underground.Outbox.Domain.DatabaseProvider;

namespace Underground.Outbox.Domain;

internal sealed class OutboxProcessor(
    OutboxServiceConfiguration config,
    IOutboxMessageDispatcher dispatcher,
    IOutboxDatabaseProvider databaseProvider,
    OutboxReflectionErrorHandler reflectionErrorHandler,
    ILogger<OutboxProcessor> logger
)
{
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
    public async Task ProcessAsync(DbContext dbContext, CancellationToken cancellationToken = default)
    {
        var fetchPartitions = CreateFetchPartitionsBlock(cancellationToken);
        var processPartitions = CreateProcessPartitionsBlock(dbContext, config.BatchSize, config.ParallelProcessingOfPartitions, cancellationToken);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        fetchPartitions.LinkTo(processPartitions, linkOptions);

        fetchPartitions.Post(0);
        fetchPartitions.Complete();
        await processPartitions.Completion;
    }

    private TransformManyBlock<int, string> CreateFetchPartitionsBlock(CancellationToken cancellationToken)
    {
        return new TransformManyBlock<int, string>(async _ => await databaseProvider.GetPartitionsAsync(cancellationToken));
    }

    private ActionBlock<string> CreateProcessPartitionsBlock(DbContext dbContext, int batchSize, int parallelProcessingOfPartitions, CancellationToken cancellationToken)
    {
        return new ActionBlock<string>(async partition =>
        {
            await databaseProvider.FetchAndUpdateMessagesWithTransactionAsync(async messages =>
            {
                logger.LogInformation("Processing {Count} outbox messages", messages.Count());
                var successIds = await CallMessageHandlersAsync(messages, dbContext, cancellationToken);
                return successIds;
            }, batchSize, cancellationToken);
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelProcessingOfPartitions });
    }

#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.

    private async Task<IEnumerable<int>> CallMessageHandlersAsync(IEnumerable<OutboxMessage> messages, DbContext dbContext, CancellationToken cancellationToken)
    {
        var successfulIds = new List<int>();

        foreach (var message in messages)
        {
            ProcessingResult result;

            try
            {
                result = await dispatcher.ExecuteAsync(message, cancellationToken);
            }
            catch (ParsingException ex)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                result = await CallErrorHandlerAsync(reflectionErrorHandler, dbContext, message, ex, cancellationToken);
            }
            catch (MessageHandlerException ex)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                result = await CallErrorHandlerAsync(ex.ErrorHandler, dbContext, message, ex.InnerException, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                result = await CallErrorHandlerAsync(reflectionErrorHandler, dbContext, message, ex, cancellationToken);
            }

            // use if/else instead of a switch to be able to break out of the foreach loop
            if (result == ProcessingResult.Success)
            {
                successfulIds.Add(message.Id);
            }
            else if (result == ProcessingResult.FailureAndContinue)
            {
                continue; // continue processing other messages
            }
            else if (result == ProcessingResult.FailureAndStop)
            {
                break; // Break out of the foreach loop (stop processing on first failure)
            }
        }

        return successfulIds;
    }

    private async Task<ProcessingResult> CallErrorHandlerAsync(IOutboxErrorHandler errorHandler, DbContext dbContext, OutboxMessage message, Exception exception, CancellationToken cancellationToken)
    {
        await errorHandler.HandleErrorAsync(dbContext, message, exception, config, cancellationToken);
        return errorHandler.ShouldStopProcessingOnError ? ProcessingResult.FailureAndStop : ProcessingResult.FailureAndContinue;
    }
}
