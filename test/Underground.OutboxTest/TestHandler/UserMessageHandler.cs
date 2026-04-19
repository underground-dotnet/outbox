using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class UserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<UserMessage>
{
    public async Task HandleAsync(UserMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        await dbContext.Users.AddAsync(new User { Name = "Testuser Success" }, cancellationToken);
    }
}
