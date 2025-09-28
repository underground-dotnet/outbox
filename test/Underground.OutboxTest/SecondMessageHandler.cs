using System;

using Underground.Outbox;

namespace Underground.OutboxTest;

public class SecondMessageHandler : IOutboxMessageHandler<SecondMessage>
{
    public static List<SecondMessage> CalledWith { get; set; } = [];

    public Task HandleAsync(SecondMessage message, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        return Task.CompletedTask;
    }
}
