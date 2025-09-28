
using Underground.Outbox;

namespace ConsoleApp;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine("received: " + message.Id);
        return Task.CompletedTask;
    }
}
