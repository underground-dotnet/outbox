using Underground.Outbox;

namespace MultiProjectLib;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        Console.WriteLine("received outbox: " + message.Id);
        return Task.CompletedTask;
    }
}
