using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Stages a row that violates a NOT NULL constraint and returns without saving, so the failure comes from
/// the save the library runs after the Handler.
/// </summary>
public class UnsavableMessageHandler(InboxOutboxDbContext dbContext)
    : IOutboxMessageHandler<UnsavableMessage>, IInboxMessageHandler<UnsavableMessage>
{
    public Task HandleAsync(UnsavableMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(new User { Name = null! });

        return Task.CompletedTask;
    }
}
