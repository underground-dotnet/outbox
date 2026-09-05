using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

public static class ProcessorExtensions
{
    extension<TEntity>(IProcessor<TEntity>) where TEntity : class, IMessage
    {
        /// <summary>
        /// Claims and handles one Head once. A claim yields one message, so a test that lines up two
        /// messages in one Group calls this twice.
        /// </summary>
        internal static async Task ProcessWithDefaultValues(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IProcessor<OutboxMessage>>();
            await processor.ProcessHeadAsync(scope, cancellationToken);
        }
    }

    extension<TEntity>(ConcurrentProcessor<TEntity> processor) where TEntity : class, IMessage
    {
        /// <summary>
        /// Drives <see cref="ConcurrentProcessor{TEntity}.ProcessNextAsync"/> on the calling thread until a
        /// claim comes back empty. Replaces waiting on the background workers, so that tests assert on a
        /// finished run instead of on a timeout. Note that a Head which failed is out of sight for its
        /// backoff and so is left behind for a later run, exactly as it is in production.
        /// </summary>
        internal async Task ProcessUntilIdleAsync(CancellationToken cancellationToken)
        {
            while (await processor.ProcessNextAsync(cancellationToken))
            {
                // keep going until nothing is left to claim
            }
        }
    }
}
