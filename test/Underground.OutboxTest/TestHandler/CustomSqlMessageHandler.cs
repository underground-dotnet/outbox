using System.Data;

using Microsoft.EntityFrameworkCore;

using Underground.Outbox;

namespace Underground.OutboxTest.TestHandler;

public class CustomSqlMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];

    public async Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);

        await dbContext.Database.ExecuteSqlAsync(
            $"""INSERT INTO "Users" ("Id", "Name") VALUES (100, 'CustomSqlUser')""",
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}
