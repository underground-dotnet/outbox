using System.Runtime.CompilerServices;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    public static List<ExampleMessage> CalledWith { get; set; } = [];
    // monitors different ids of the handler instances
    public static ISet<string> ObjectIds { get; set; } = new HashSet<string>();

    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Add(message);
        ObjectIds.Add($"{RuntimeHelpers.GetHashCode(this)}");
        return Task.CompletedTask;
    }
}
