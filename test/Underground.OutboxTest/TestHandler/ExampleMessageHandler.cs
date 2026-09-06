using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class ExampleMessageHandler : IOutboxMessageHandler<ExampleMessage>
{
    // the processors handle messages on background threads while a test reads these, so both have to
    // be concurrent collections - a plain List or HashSet lets the test see a half-written collection
    public static ConcurrentQueue<ExampleMessage> CalledWith { get; } = new();
    // monitors different ids of the handler instances
    public static ConcurrentDictionary<string, byte> ObjectIds { get; } = new(StringComparer.Ordinal);

    public Task HandleAsync(ExampleMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.Enqueue(message);
        ObjectIds.TryAdd($"{RuntimeHelpers.GetHashCode(this)}", 0);
        return Task.CompletedTask;
    }
}
