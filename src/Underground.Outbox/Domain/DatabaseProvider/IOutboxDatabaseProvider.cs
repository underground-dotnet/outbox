using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.DatabaseProvider;

public interface IOutboxDatabaseProvider
{
    public Task<IEnumerable<string>> GetPartitionsAsync(CancellationToken cancellationToken);
    public Task FetchAndUpdateMessagesWithTransactionAsync(Func<IEnumerable<OutboxMessage>, Task<IEnumerable<int>>> action, int batchSize, CancellationToken cancellationToken);
}
