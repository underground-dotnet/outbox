using Underground;

namespace UndergroundTest;

public class ExampleMessageHandler : IMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];

    public Task Handle(ExampleMessage message)
    {
        CalledWith.Add(message);
        return Task.CompletedTask;
    }
}
