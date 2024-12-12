using Underground;

namespace ConsoleApp;

public class ExampleMessageHandler : IMessageHandler<ExampleMessage>
{
    public Task Handle(ExampleMessage message)
    {
        throw new NotImplementedException();
    }
}
