using System.Text.Json;
using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;
using System.Reflection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks.Dataflow;
using Underground.Outbox.ErrorHandler;

namespace Underground.Outbox.Domain;

internal enum ProcessingResult
{
    Success,
    FailureAndStop,
    FailureAndContinue
}

internal sealed class OutboxProcessor(OutboxServiceConfiguration config, IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
{
    private static readonly ConcurrentDictionary<MessageType, HandlerType> HandlerTypeDictionary = new();
    private static readonly ConcurrentDictionary<MessageType, MethodInfo?> HandleMethodDictionary = new();
    private OutboxReflectionErrorHandler _reflectionErrorHandler = null!;

#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var dbContextType = config.DbContextType ?? throw new NoDbContextAssignedException();

        using var scope = scopeFactory.CreateScope();
        _reflectionErrorHandler = scope.ServiceProvider.GetRequiredService<OutboxReflectionErrorHandler>();
        using var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(dbContextType);

        var fetchPartitions = CreateFetchPartitionsBlock(dbContext, cancellationToken);
        var processPartitions = CreateProcessPartitionsBlock(scope, dbContext, config.BatchSize, config.ParallelProcessingOfPartitions, cancellationToken);

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

    private ActionBlock<string> CreateProcessPartitionsBlock(IServiceScope scope, DbContext dbContext, int batchSize, int parallelProcessingOfPartitions, CancellationToken cancellationToken)
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

            var successIds = await CallMessageHandlersAsync(messages, scope.ServiceProvider, dbContext, cancellationToken);

            // mark as processed
            await dbContext.Database.ExecuteSqlRawAsync(
                $"""UPDATE {config.FullTableName} SET completed = true WHERE "id" = ANY(@ids)""",
                new NpgsqlParameter("@ids", successIds.ToArray())
            );
            await transaction.CommitAsync(cancellationToken);
        }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = parallelProcessingOfPartitions });
    }

#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.

    private async Task<IEnumerable<int>> CallMessageHandlersAsync(IEnumerable<OutboxMessage> messages, IServiceProvider services, DbContext dbContext, CancellationToken cancellationToken)
    {
        var successfulIds = new List<int>();

        foreach (var message in messages)
        {
            ProcessingResult result;

            try
            {
                result = await ProcessMessageAsync(message, services, cancellationToken);
            }
            catch (ParsingException ex)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                result = await CallErrorHandlerAsync(_reflectionErrorHandler, dbContext, message, ex, cancellationToken);
            }
            catch (MessageHandlerException ex)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                result = await CallErrorHandlerAsync(ex.ErrorHandler, dbContext, message, ex.InnerException, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing message {MessageId}", message.Id);
                result = await CallErrorHandlerAsync(_reflectionErrorHandler, dbContext, message, ex, cancellationToken);
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

    private static async Task<ProcessingResult> ProcessMessageAsync(OutboxMessage message, IServiceProvider services, CancellationToken cancellationToken)
    {
        HandlerType? type = HandlerType.GetType(message.Type) ?? throw new ParsingException($"Cannot resolve type '{message.Type}' of message: {message.Id}");

        var fullEvent = JsonSerializer.Deserialize(message.Data, type) ?? throw new ParsingException($"Cannot parse event body '{message.Data} of message: {message.Id}");

        // construct concrete handler type based on the event type and cache it so that the type is only resolved once
        // idea from: https://www.youtube.com/watch?v=5yLIzis9Qr0 it also shows more improvements we could do here
        Type interfaceType = HandlerTypeDictionary.GetOrAdd(
            fullEvent.GetType(),
            eventType => typeof(IOutboxMessageHandler<>).MakeGenericType(eventType)
        );

        // TODO: will throw when handler not found...
        // Console.WriteLine($"Handler not found for type {type}; all handlers: {string.Join(", ", config.Handlers.Keys)}");
        var handlerObject = services.GetRequiredService(interfaceType);

        // resolve and invoke `Handle` method
        // resolve the method info from the type and cache it so that the method is only resolved once
        MethodInfo? method = HandleMethodDictionary.GetOrAdd(
            interfaceType,
            type => type.GetMethod("HandleAsync")
        );

        if (method is null)
        {
            throw new ParsingException($"Cannot find 'HandleAsync' method in handler type: {interfaceType.Name}");
        }

        try
        {
            var task = (Task?)method.Invoke(handlerObject, [fullEvent, cancellationToken]);
            if (task is not null)
            {
                await task;
            }
        }
        catch (TargetInvocationException ex)
        {
            var errorHandlerProperty = interfaceType.GetProperty("ErrorHandler") ?? throw new ParsingException($"Cannot get errorHandler for type '{message.Type} of message: {message.Id}");
            var errorHandler = errorHandlerProperty.GetValue(handlerObject) as IOutboxErrorHandler ?? throw new ParsingException($"Cannot get errorHandler instance for type '{message.Type} of message: {message.Id}");

            throw new MessageHandlerException(
                $"Error processing message {message.Id} with handler {handlerObject.GetType().Name}",
                ex.InnerException ?? ex,
                errorHandler
            );
        }

        return ProcessingResult.Success;
    }

    private async Task<ProcessingResult> CallErrorHandlerAsync(IOutboxErrorHandler errorHandler, DbContext dbContext, OutboxMessage message, Exception exception, CancellationToken cancellationToken)
    {
        await errorHandler.HandleErrorAsync(dbContext, message, exception, config, cancellationToken);
        return errorHandler.ShouldStopProcessingOnError ? ProcessingResult.FailureAndStop : ProcessingResult.FailureAndContinue;
    }
}
