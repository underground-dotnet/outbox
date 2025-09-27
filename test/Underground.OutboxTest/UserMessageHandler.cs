using System;
using System.Data;

using Underground.Outbox;

namespace Underground.OutboxTest;

public class UserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];
    public IOutboxErrorHandler ErrorHandler => new MockErrorHandler();

    public async Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);

        await dbContext.Users.AddAsync(new User { Name = "Testuser" }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}