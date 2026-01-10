using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Domain;

internal sealed class Processor<TEntity>(
    ServiceConfiguration config,
    IServiceScopeFactory scopeFactory,
    IMessageDispatcher<TEntity> dispatcher,
    ILogger<Processor<TEntity>> logger
    ) where TEntity : class, IMessage
{
    private readonly ServiceConfiguration _config = config;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IMessageDispatcher<TEntity> _dispatcher = dispatcher;
    private readonly ILogger<Processor<TEntity>> _logger = logger;
    private readonly Lock _lock = new();
    private Task? _currentTask = null;

    internal Task ProcessAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_currentTask is not null && !_currentTask.IsCompleted)
            {
                // still running
                return _currentTask;
            }

            _currentTask = FetchPartitionsAndProcess(cancellationToken);

            return _currentTask;
        }
    }

    private async Task FetchPartitionsAndProcess(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

        var partitions = await dbContext.Set<TEntity>()
            .Where(message => message.ProcessedAt == null)
            .Select(message => message.PartitionKey)
            .Distinct()
            .AsNoTracking()
            .ToListAsync(cancellationToken: cancellationToken);

        var parallellOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _config.ParallelProcessingOfPartitions,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(partitions, parallellOptions, async (partition, ct) =>
        {
            await ProcessPartitionAsync(partition, ct);
        });
    }

    private async Task ProcessPartitionAsync(string partition, CancellationToken cancellationToken)
    {
        // repeat until no more messages are found for this partition
        var messagesProcessed = true;
        while (messagesProcessed && !cancellationToken.IsCancellationRequested)
        {
            messagesProcessed = await ProcessMessagesAsync(partition, _config.BatchSize, cancellationToken);
        }
    }

    /// <summary>
    /// Processes a batch of messages for the given partition using a new scope and DbContext.
    /// </summary>
    /// <param name="partition"></param>
    /// <param name="batchSize"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A boolean indicating if any messages were found or if the outbox is empty. It only returns true if all found messages were processed successfully.</returns>
    private async Task<bool> ProcessMessagesAsync(string partition, int batchSize, CancellationToken cancellationToken)
    {
        // use separate scope & context for each partition
        using var scope = _scopeFactory.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // no need for "SELECT FOR UPDATE" since we have a distributed lock which only has one runner active
        var messages = await dbContext.Set<TEntity>()
            .Where(message => message.ProcessedAt == null && message.PartitionKey == partition)
            .OrderBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken: cancellationToken);

        _logger.LogInformation("Processing {Count} messages in {Type} for partition '{Partition}'", messages.Count, typeof(TEntity), partition);

        var successIds = await CallMessageHandlersAsync(messages, scope, dbContext);

        // mark as processed
        await dbContext.Set<TEntity>()
            .Where(m => successIds.Contains(m.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // remove tracked entities to avoid memory leaks
        dbContext.ChangeTracker.Clear();

        return messages.Count > 0 && messages.Count == successIds.Count();
    }

    // TODO: use cancellation token
    private async Task<IEnumerable<long>> CallMessageHandlersAsync(IEnumerable<TEntity> messages, IServiceScope scope, IOutboxDbContext dbContext)
    {
        var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

        var transaction = dbContext.Database.CurrentTransaction!;
        var successfulIds = new List<long>();

        foreach (var message in messages)
        {
            var savepointName = $"processing_message_{message.Id}";
            await transaction.CreateSavepointAsync(savepointName);
            Exception? exception = null;

            try
            {
                await _dispatcher.ExecuteAsync(scope, message);
                // persist all changes from the handler. (in case the handler forgot to call SaveChanges)
                await dbContext.SaveChangesAsync();
                successfulIds.Add(message.Id);
                await transaction.ReleaseSavepointAsync(savepointName);
            }
            catch (MessageHandlerException ex)
            {
                _logger.LogError(ex, "Error processing message {MessageId} in handler", message.Id);
                exception = ex;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing message {MessageId}. Probably a reflection issue.", message.Id);
                exception = ex;
            }

            if (exception is not null)
            {
                // clear all tracked entities, because the batch processing failed. The ErrorHandler can then use the clean context to perform db operations.
                dbContext.ChangeTracker.Clear();
                await transaction.RollbackToSavepointAsync(savepointName);

                if (exception is MessageHandlerException ex)
                {
                    await processHandlerException.ExecuteAsync(ex, message, dbContext);
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

    private static async Task IncrementRetryCountAsync(IOutboxDbContext dbContext, IMessage message)
    {
        await dbContext.Set<TEntity>()
            .Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(m => m.RetryCount, m => m.RetryCount + 1));
    }
}
