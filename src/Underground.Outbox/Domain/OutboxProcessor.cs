using Microsoft.EntityFrameworkCore;
using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;
using Underground.Outbox.ErrorHandler;
using Underground.Outbox.Domain.Dispatcher;

namespace Underground.Outbox.Domain;

internal sealed class OutboxProcessor(
    OutboxServiceConfiguration config,
    IMessageDispatcher dispatcher,
    OutboxReflectionErrorHandler reflectionErrorHandler,
    ILogger<OutboxProcessor> logger
)
{
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
    public async Task ProcessAsync(DbContext dbContext, CancellationToken cancellationToken = default)
    {
        var fetchPartitions = CreateFetchPartitionsBlock(dbContext, cancellationToken);
        var processPartitions = CreateProcessPartitionsBlock(dbContext, config.BatchSize, config.ParallelProcessingOfPartitions, cancellationToken);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        fetchPartitions.LinkTo(processPartitions, linkOptions);

        fetchPartitions.Post(0);
        fetchPartitions.Complete();
        await processPartitions.Completion;
    }

    private TransformManyBlock<int, string> CreateFetchPartitionsBlock(DbContext dbContext, CancellationToken cancellationToken)
    {
        return new TransformManyBlock<int, string>(async _ =>
        {
            var partitions = await dbContext.Database
                .SqlQueryRaw<string>($"""SELECT DISTINCT(partition_key) FROM {config.FullTableName} WHERE completed = false""")
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return partitions;
        });
    }

    private ActionBlock<string> CreateProcessPartitionsBlock(DbContext dbContext, int batchSize, int parallelProcessingOfPartitions, CancellationToken cancellationToken)
    {
        return new ActionBlock<string>(async partition =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // raw query is needed to inject tablename
            var batchSizeValue = new NpgsqlParameter("@batchSize", batchSize);

            // use NOWAIT instead of SKIP LOCKED to avoid deadlocks when multiple instances are running and to keep order guaranteed
            var messages = await dbContext.Database.SqlQueryRaw<OutboxMessage>(
                $"""SELECT * FROM {config.FullTableName} WHERE completed = false ORDER BY "id" FOR UPDATE NOWAIT LIMIT @batchSize""", batchSizeValue
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);

            logger.LogInformation("Processing {Count} outbox messages", messages.Count);

            var successIds = await CallMessageHandlersAsync(messages, dbContext, cancellationToken);

            // mark as processed
            await dbContext.Database.ExecuteSqlRawAsync(
                $"""UPDATE {config.FullTableName} SET completed = true WHERE "id" = ANY(@ids)""",
                new NpgsqlParameter("@ids", successIds.ToArray())
            );
            await transaction.CommitAsync(cancellationToken);
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelProcessingOfPartitions });
    }

#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.

    private async Task<IEnumerable<int>> CallMessageHandlersAsync(IEnumerable<OutboxMessage> messages, DbContext dbContext, CancellationToken cancellationToken)
    {
        var successfulIds = new List<int>();

        foreach (var message in messages)
        {
            ProcessingResult result;
            var transaction = dbContext.Database.CurrentTransaction!;
            var savepointName = $"before_message_{message.Id}";

            try
            {
                await transaction.CreateSavepointAsync(savepointName, cancellationToken);
                result = await dispatcher.ExecuteAsync(message, cancellationToken);
            }
            catch (MessageHandlerException ex)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken);
                result = await CallErrorHandlerAsync(ex.ErrorHandler, dbContext, message, ex.InnerException, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken);
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
