using Microsoft.Extensions.DependencyInjection;

using Underground;
using Underground.Inbox;

namespace UndergroundTest;

public class UnitTest1
{
    [Fact]
    public async Task CallsHandlerToHandleTheMessage()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<ExampleMessageHandler>();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var inbox = new InMemoryInbox();
        await inbox.AddAsync(new InboxMessage(Guid.NewGuid(), DateTime.UtcNow, new ExampleMessage(1)));

        Dictionary<Type, HandlerDescriptor> handlers = new()
        {
            { typeof(ExampleMessage), new HandlerDescriptor(typeof(ExampleMessageHandler)) }
        };
        var processor = new InboxProcessor(inbox, handlers, serviceProvider);

        // Act
        await processor.ProcessAsync();

        // Assert
        Assert.Single(ExampleMessageHandler.CalledWith);
    }
}
