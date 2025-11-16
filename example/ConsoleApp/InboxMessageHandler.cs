
using Underground.Outbox;

namespace ConsoleApp;

public class InboxMessageHandler : IInboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine("received inbox: " + message.Id);
        return Task.CompletedTask;
    }
}
