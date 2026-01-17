using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class UserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<SecondMessage>
{
    public async Task HandleAsync(SecondMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        await dbContext.Users.AddAsync(new User { Name = "Testuser Success" }, cancellationToken);
    }
}
