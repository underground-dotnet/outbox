using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class MultipleMessagesHandler : IOutboxMessageHandler<MultiMessageA>, IOutboxMessageHandler<MultiMessageB>
{
    public static IList<MultiMessageA> CalledWithA { get; set; } = [];
    public static IList<MultiMessageB> CalledWithB { get; set; } = [];

    public Task HandleAsync(MultiMessageA message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithA.Add(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(MultiMessageB message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWithB.Add(message);
        return Task.CompletedTask;
    }
}

#pragma warning disable MA0048 // File name must match type name
public record MultiMessageA(int Id);
public record MultiMessageB(int Id);
#pragma warning restore MA0048 // File name must match type name
