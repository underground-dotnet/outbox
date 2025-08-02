using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.ErrorHandler;

#pragma warning disable EF1002 // Risk of vulnerability to SQL injection.
public class OutboxRetryErrorHandler : IOutboxErrorHandler
{
    public bool ShouldStopProcessingOnError => true;

    public async Task HandleErrorAsync(DbContext dbContext, OutboxMessage message, Exception exception, OutboxServiceConfiguration config, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            $"""UPDATE {config.FullTableName} SET retry_count = retry_count + 1 WHERE "id" = {message.Id}""",
            cancellationToken
        );
    }
}
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection.
