using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class MultipleMessagesHandler : IOutboxMessageHandler<MultiMessageA>, IOutboxMessageHandler<MultiMessageB>
{
    public static List<MultiMessageA> CalledWithA { get; set; } = [];
    public static List<MultiMessageB> CalledWithB { get; set; } = [];

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

public record MultiMessageA(int Id);
public record MultiMessageB(int Id);