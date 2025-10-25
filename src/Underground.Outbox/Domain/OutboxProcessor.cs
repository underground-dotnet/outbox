using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.DependencyInjection;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Domain;

internal sealed class OutboxProcessor(
    OutboxServiceConfiguration config,
    IServiceScopeFactory scopeFactory,
    IMessageDispatcher dispatcher,
    ILogger<OutboxProcessor> logger
)
{
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        // TODO: move setup to constructor, then we can allow push based processing as well
        var fetchPartitions = CreateFetchPartitionsBlock(cancellationToken);
        var processPartitions = CreateProcessPartitionsBlock(config.BatchSize, config.ParallelProcessingOfPartitions, cancellationToken);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        fetchPartitions.LinkTo(processPartitions, linkOptions);

        fetchPartitions.Post(0);
        fetchPartitions.Complete();
        await processPartitions.Completion;
    }

    private TransformManyBlock<int, string> CreateFetchPartitionsBlock(CancellationToken cancellationToken)
    {
        return new TransformManyBlock<int, string>(async _ =>
        {
            using var scope = scopeFactory.CreateScope();
            await using var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

            var partitions = await dbContext.OutboxMessages
                .Where(message => message.ProcessedAt == null)
                .Select(message => message.PartitionKey)
                .Distinct()
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return partitions;
        });
    }

    private ActionBlock<string> CreateProcessPartitionsBlock(int batchSize, int parallelProcessingOfPartitions, CancellationToken cancellationToken)
    {
        return new ActionBlock<string>(async partition =>
        {
            // use separate scope & context for each partition
            using var scope = scopeFactory.CreateScope();
            await using var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // no need for "SELECT FOR UPDATE" since we have a distributed lock which only has one runner active
            var messages = await dbContext.OutboxMessages
                .Where(message => message.ProcessedAt == null && message.PartitionKey == partition)
                .OrderBy(message => message.Id)
                .Take(batchSize)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            logger.LogInformation("Processing {Count} outbox messages for partition '{Partition}'", messages.Count, partition);

            var successIds = await CallMessageHandlersAsync(messages, scope, dbContext, cancellationToken);

            // mark as processed
            await dbContext.OutboxMessages
                .Where(m => successIds.Contains(m.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelProcessingOfPartitions });
    }

    private async Task<IEnumerable<int>> CallMessageHandlersAsync(IEnumerable<OutboxMessage> messages, IServiceScope scope, IOutboxDbContext dbContext, CancellationToken cancellationToken)
    {
        var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler>();

        var savepointName = $"batch_processing";
        var transaction = dbContext.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(savepointName, cancellationToken);

        var successfulIds = new List<int>();
        foreach (var message in messages)
        {
            // TODO: if the current message has failed before and is not the first one to process then stop here and commit the previous messages.
            Exception? exception = null;

            try
            {
                await dispatcher.ExecuteAsync(scope, message, cancellationToken);
                successfulIds.Add(message.Id);
            }
            catch (MessageHandlerException ex)
            {
                logger.LogError(ex, "Error processing message {MessageId} in handler", message.Id);
                exception = ex;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing message {MessageId}. Probably a reflection issue.", message.Id);
                exception = ex;
            }

            if (exception is not null)
            {
                // clear all tracked entities, because the batch processing failed
                dbContext.ChangeTracker.Clear();
                successfulIds.Clear();
                await transaction.RollbackToSavepointAsync(savepointName, cancellationToken);

                if (exception is MessageHandlerException ex)
                {
                    await processHandlerException.ExecuteAsync(ex, message, dbContext, cancellationToken);
                }

                // TODO: decide if max retry count is reached or if a retry makes sense
                // TODO: remove or move to processHandlerException
                await IncrementRetryCountAsync(dbContext, message, cancellationToken);

                // Break out of the foreach loop (stop processing on first failure)
                break;
            }
        }

        return successfulIds;
    }

    private static async Task IncrementRetryCountAsync(IOutboxDbContext dbContext, OutboxMessage message, CancellationToken cancellationToken)
    {
        await dbContext.OutboxMessages
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(m => m.RetryCount, m => m.RetryCount + 1), cancellationToken);
    }
}
