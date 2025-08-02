using System;

using Underground.Outbox;
using Underground.Outbox.ErrorHandler;

namespace Underground.OutboxTest;

public class SecondMessageHandler : IOutboxMessageHandler<SecondMessage>
{
    public static List<SecondMessage> CalledWith { get; set; } = [];

    public IOutboxErrorHandler ErrorHandler { get; } = new OutboxRetryErrorHandler();

    public Task HandleAsync(SecondMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        return Task.CompletedTask;
    }
}
