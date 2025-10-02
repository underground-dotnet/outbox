using Microsoft.EntityFrameworkCore;
using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;
using Underground.Outbox.Domain.Dispatcher;
using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Domain;

internal sealed class OutboxProcessor(
    OutboxServiceConfiguration config,
    IServiceScopeFactory scopeFactory,
    IMessageDispatcher dispatcher,
    ILogger<OutboxProcessor> logger
)
{
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
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
            await using var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(config.DbContextType ?? throw new NoDbContextAssignedException());

            var partitions = await dbContext.Database
                .SqlQueryRaw<string>($"""SELECT DISTINCT(partition_key) FROM {config.FullTableName} WHERE completed = false""")
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
            await using var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(config.DbContextType ?? throw new NoDbContextAssignedException());

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

            var successIds = await CallMessageHandlersAsync(messages, scope, dbContext, cancellationToken);

            // mark as processed
            await dbContext.Database.ExecuteSqlRawAsync(
                $"""UPDATE {config.FullTableName} SET completed = true WHERE "id" = ANY(@ids)""",
                new NpgsqlParameter("@ids", successIds.ToArray())
            );
            await transaction.CommitAsync(cancellationToken);
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelProcessingOfPartitions });
    }

#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.

    private async Task<IEnumerable<int>> CallMessageHandlersAsync(IEnumerable<OutboxMessage> messages, IServiceScope scope, DbContext dbContext, CancellationToken cancellationToken)
    {
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

                // TODO: decide if max retry count is reached or if a retry makes sense
                await IncrementRetryCountAsync(dbContext, message, cancellationToken);
                // Break out of the foreach loop (stop processing on first failure)
                break;
            }
        }

        return successfulIds;
    }

    private async Task IncrementRetryCountAsync(DbContext dbContext, OutboxMessage message, CancellationToken cancellationToken)
    {
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
        await dbContext.Database.ExecuteSqlRawAsync(
            $"""UPDATE {config.FullTableName} SET retry_count = retry_count + 1 WHERE "id" = {message.Id}""",
            cancellationToken
        );
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.
    }
}
