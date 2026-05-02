using Microsoft.EntityFrameworkCore;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain;

[Collection("ExampleMessageHandler Collection")]
public class DeleteProcessedMessagesTests(ITestOutputHelper testOutputHelper) : DatabaseTest(testOutputHelper)
{
    [Fact]
    public async Task ExecuteAsync_DeletesProcessedRowsJustOutsideRetentionWindow_AndKeepsRowsInsideIt()
    {
        // Arrange
        var context = CreateDbContext();
        var retention = TimeSpan.FromSeconds(30);
        var outsideRetentionId = Guid.NewGuid();
        var insideRetentionId = Guid.NewGuid();
        var unprocessedId = Guid.NewGuid();
        var referenceTime = DateTime.UtcNow;

        context.OutboxMessages.AddRange(
            new OutboxMessage(outsideRetentionId, referenceTime.AddMinutes(-1), new ExampleMessage(1))
            {
                ProcessedAt = referenceTime - retention - TimeSpan.FromSeconds(5)
            },
            new OutboxMessage(insideRetentionId, referenceTime.AddMinutes(-1), new ExampleMessage(2))
            {
                ProcessedAt = referenceTime - retention + TimeSpan.FromSeconds(5)
            },
            new OutboxMessage(unprocessedId, referenceTime.AddMinutes(-1), new ExampleMessage(3))
        );
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var configuration = new OutboxServiceConfiguration
        {
            ProcessedMessageRetention = retention
        };
        var useCase = new DeleteProcessedMessages<OutboxMessage>(context, configuration);
        var deletedCount = await useCase.ExecuteAsync(TestContext.Current.CancellationToken);

        // Assert
        var remainingMessages = await context.OutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, deletedCount);
        Assert.Equal(2, remainingMessages.Count);
        Assert.DoesNotContain(remainingMessages, message => message.EventId == outsideRetentionId);
        Assert.Contains(remainingMessages, message => message.EventId == insideRetentionId && message.ProcessedAt != null);
        Assert.Contains(remainingMessages, message => message.EventId == unprocessedId && message.ProcessedAt == null);
    }
}
