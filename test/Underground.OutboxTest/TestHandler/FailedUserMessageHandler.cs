using System.Data;

using Microsoft.EntityFrameworkCore.Storage;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class FailedUserMessageHandler(TestDbContext dbContext) : IOutboxMessageHandler<FailedUserMessage>
{
    /// <summary>
    /// The transaction the handler's own DbContext was in, or <see langword="null"/> if there was none.
    /// Null on its own says nothing - the handler may simply never have run - which is what
    /// <see cref="WasCalled"/> is for.
    /// </summary>
    public static IDbContextTransaction? CalledWithTransaction { get; set; }

    /// <summary>Whether the handler ran at all, which is what makes a null transaction mean something.</summary>
    public static bool WasCalled { get; set; }

    /// <summary>Clears the statics between tests, since they are shared across the whole collection.</summary>
    public static void Reset()
    {
        CalledWithTransaction = null;
        WasCalled = false;
    }

    public async Task HandleAsync(FailedUserMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithTransaction = dbContext.Database.CurrentTransaction;
        WasCalled = true;

        await dbContext.Users.AddAsync(new User { Name = "Testuser" }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        throw new DataException("Failed to handle message");
    }
}
