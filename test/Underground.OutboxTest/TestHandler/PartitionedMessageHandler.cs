
using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class PartitionedMessageHandler : IOutboxMessageHandler<PartitionedMessage>
{
    public static ConcurrentDictionary<string, List<int>> CalledWith { get; set; } = [];
    public static int TotalCount => CalledWith.Values.Sum(list => list.Count);

    public Task HandleAsync(PartitionedMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.AddOrUpdate(
            GetPartitionKey(message),
            _ => [message.Id],
            (_, list) =>
            {
                list.Add(message.Id);
                return list;
            });

        return Task.CompletedTask;
    }

    private static string GetPartitionKey(PartitionedMessage message)
    {
        return (message.Id % 4) switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            _ => throw new InvalidOperationException("This should never happen"),
        };
    }
}