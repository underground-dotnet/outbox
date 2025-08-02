using System;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Data;

namespace Underground.Outbox;

public interface IOutbox
{
    public Task AddMessageAsync(DbContext context, OutboxMessage message);

    public Task AddMessagesAsync(DbContext context, IEnumerable<OutboxMessage> messages);
}
