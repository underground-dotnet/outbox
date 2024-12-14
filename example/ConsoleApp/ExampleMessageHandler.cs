using Underground;

namespace ConsoleApp;

public class ExampleMessageHandler : IMessageHandler<ExampleMessage>
{
    public Task Handle(ExampleMessage message)
    {
        Console.WriteLine("received: " + message.Id);
        return Task.CompletedTask;
    }
}
