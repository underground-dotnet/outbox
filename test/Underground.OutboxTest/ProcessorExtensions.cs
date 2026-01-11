using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest;

public static class Processor
{
    extension<TEntity>(Processor<TEntity>) where TEntity : class, IMessage
    {
        internal static async Task ProcessWithDefaultValues(IServiceProvider serviceProvider, CancellationToken cancellationToken, string partition = "default")
        {
            using var scope = serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<Processor<OutboxMessage>>();
            await processor.ProcessMessagesAsync(partition, 5, scope, cancellationToken);
        }
    }
}
