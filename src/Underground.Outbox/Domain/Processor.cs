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

internal sealed class Processor<TEntity> where TEntity : class, IMessage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageDispatcher<TEntity> _dispatcher;
    private readonly ILogger<Processor<TEntity>> _logger;
    private readonly TransformManyBlock<int, string> _processFlow;
    private readonly Lock _lock = new();
    private TaskCompletionSource? _currentProcessingTask;
    private int _activePartitions;

    public Processor(
        ServiceConfiguration config,
        IServiceScopeFactory scopeFactory,
        IMessageDispatcher<TEntity> dispatcher,
        ILogger<Processor<TEntity>> logger
    )
    {
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _logger = logger;

        // setup processing flow
        _processFlow = CreateFetchPartitionsBlock();
        var processPartitions = CreateProcessPartitionsBlock(config.BatchSize, config.ParallelProcessingOfPartitions);

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        _processFlow.LinkTo(processPartitions, linkOptions);
    }

    internal void Process()
    {
        _processFlow.Post(0);
    }

    internal Task ProcessAsync()
    {
        lock (_lock)
        {
            if (_currentProcessingTask is not null && !_currentProcessingTask.Task.IsCompleted)
            {
                // still running
                return _currentProcessingTask.Task;
            }

            _currentProcessingTask = new TaskCompletionSource();

            _processFlow.Post(0);

            return _currentProcessingTask.Task;
        }
    }

    private TransformManyBlock<int, string> CreateFetchPartitionsBlock()
    {
        return new TransformManyBlock<int, string>(async _ =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await using var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

                var partitions = await dbContext.Set<TEntity>()
                    .Where(message => message.ProcessedAt == null)
                    .Select(message => message.PartitionKey)
                    .Distinct()
                    .AsNoTracking()
                    .ToListAsync();

                if (_activePartitions == 0 && partitions.Count == 0)
                {
                    // processing is completed and no more partitions are found
                    _currentProcessingTask?.SetResult();
                }

                lock (_lock)
                {
                    _activePartitions = partitions.Count;
                }

                return partitions;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error fetching partitions for processing.");
                _currentProcessingTask?.SetException(ex);
                return [];
            }
        },
        // limit capacity to 1 to avoid multiple fetches at the same time
        new ExecutionDataflowBlockOptions { BoundedCapacity = 1 });
    }

    private ActionBlock<string> CreateProcessPartitionsBlock(int batchSize, int parallelProcessingOfPartitions)
    {
        return new ActionBlock<string>(async partition =>
        {
            try
            {
                // use separate scope & context for each partition
                using var scope = _scopeFactory.CreateScope();
                await using var dbContext = scope.ServiceProvider.GetRequiredService<IOutboxDbContext>();

                await using var transaction = await dbContext.Database.BeginTransactionAsync();

                // TODO: repeat until no more messages are found for this partition
                // no need for "SELECT FOR UPDATE" since we have a distributed lock which only has one runner active
                var messages = await dbContext.Set<TEntity>()
                    .Where(message => message.ProcessedAt == null && message.PartitionKey == partition)
                    .OrderBy(message => message.Id)
                    .Take(batchSize)
                    .AsNoTracking()
                    .ToListAsync();

                _logger.LogInformation("Processing {Count} messages in {Type} for partition '{Partition}'", messages.Count, typeof(TEntity), partition);

                var successIds = await CallMessageHandlersAsync(messages, scope, dbContext);

                // mark as processed
                await dbContext.Set<TEntity>()
                    .Where(m => successIds.Contains(m.Id))
                    .ExecuteUpdateAsync(update => update.SetProperty(m => m.ProcessedAt, DateTime.UtcNow));
                await transaction.CommitAsync();

            }
            finally
            {
                lock (_lock)
                {
                    _activePartitions--;
                    if (_activePartitions == 0)
                    {
                        _currentProcessingTask?.SetResult();
                    }
                }
            }
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelProcessingOfPartitions, BoundedCapacity = 1 });
    }

    private async Task<IEnumerable<int>> CallMessageHandlersAsync(IEnumerable<TEntity> messages, IServiceScope scope, IOutboxDbContext dbContext)
    {
        var processHandlerException = scope.ServiceProvider.GetRequiredService<ProcessExceptionFromHandler<TEntity>>();

        var savepointName = $"batch_processing";
        var transaction = dbContext.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(savepointName);

        var successfulIds = new List<int>();
        foreach (var message in messages)
        {
            // TODO: if the current message has failed before and is not the first one to process then stop here and commit the previous messages.
            Exception? exception = null;

            try
            {
                await _dispatcher.ExecuteAsync(scope, message);
                successfulIds.Add(message.Id);
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
                // clear all tracked entities, because the batch processing failed
                dbContext.ChangeTracker.Clear();
                successfulIds.Clear();
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
