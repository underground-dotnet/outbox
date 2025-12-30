using Underground.Outbox;

namespace Underground.OutboxTest.TestHandler;

public class UserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<SecondMessage>
{
    public async Task HandleAsync(SecondMessage message, CancellationToken cancellationToken)
    {
        await dbContext.Users.AddAsync(new User { Name = "Testuser Success" }, cancellationToken);
    }
}
