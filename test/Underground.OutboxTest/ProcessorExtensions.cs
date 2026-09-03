using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

public static class ProcessorExtensions
{
    extension<TEntity>(Processor<TEntity>) where TEntity : class, IMessage
    {
        internal static async Task ProcessWithDefaultValues(IServiceProvider serviceProvider, CancellationToken cancellationToken, string groupKey = "default")
        {
            using var scope = serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<Processor<OutboxMessage>>();
            await processor.ProcessMessagesAsync(groupKey, 5, scope, cancellationToken);
        }
    }

    extension<TEntity>(ConcurrentProcessor<TEntity> processor) where TEntity : class, IMessage
    {
        /// <summary>
        /// Schedules a processing run and drives <see cref="ConcurrentProcessor{TEntity}.ProcessNextAsync"/>
        /// on the calling thread until no Group is left to take. Replaces waiting on the background workers,
        /// so that tests assert on a finished run instead of on a timeout. Note that a Group whose batch
        /// failed is left behind for the next run, exactly as it is in production.
        /// </summary>
        internal async Task ProcessUntilIdleAsync(CancellationToken cancellationToken)
        {
            processor.ScheduleProcessingRun();

            while (await processor.ProcessNextAsync(cancellationToken))
            {
                // keep going until no Group is left to process
            }
        }
    }
}
