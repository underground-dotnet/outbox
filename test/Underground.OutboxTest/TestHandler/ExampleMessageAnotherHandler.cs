using Underground.Outbox;

namespace Underground.OutboxTest.TestHandler;

public class ExampleMessageAnotherHandler : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];

    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        return Task.CompletedTask;
    }
}
