using Underground.Outbox;
using Underground.Outbox.ErrorHandler;

namespace Underground.OutboxTest;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];

    public IOutboxErrorHandler ErrorHandler { get; } = new OutboxRetryErrorHandler();

    public Task HandleAsync(ExampleMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        return Task.CompletedTask;
    }
}
