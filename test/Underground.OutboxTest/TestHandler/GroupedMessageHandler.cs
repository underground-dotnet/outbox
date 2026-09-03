
using System.Collections.Concurrent;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestHandler;

public class GroupedMessageHandler : IOutboxMessageHandler<GroupedMessage>
{
    public static ConcurrentDictionary<string, List<int>> CalledWith { get; set; } = [];
    public static int TotalCount => CalledWith.Values.Sum(list => list.Count);

    /// <summary>
    /// Number of Groups that have to be handled at the same time before any handler is allowed to return.
    /// Zero disables the barrier, which is what every test that drives the processor on one thread needs.
    /// </summary>
    public static int ExpectedConcurrentGroups { get; set; }

    /// <summary>
    /// Completes once <see cref="ExpectedConcurrentGroups"/> Groups are being handled simultaneously.
    /// It can only complete if distinct Groups really are handled concurrently, so a test can await it
    /// instead of inferring concurrency from counts a serial run would produce just as well.
    /// </summary>
    public static TaskCompletionSource GroupsHandledConcurrently { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task HandleAsync(GroupedMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        CalledWith.AddOrUpdate(
            GetGroupKey(message),
            _ => [message.Id],
            (_, list) =>
            {
                list.Add(message.Id);
                return list;
            });

        if (ExpectedConcurrentGroups <= 0)
        {
            return;
        }

        if (CalledWith.Count >= ExpectedConcurrentGroups)
        {
            GroupsHandledConcurrently.TrySetResult();
        }

        // hold this Group until every other Group is being handled too. The timeout only ever elapses
        // when they are not, and then it releases the workers so that the test can fail and shut down.
        await GroupsHandledConcurrently.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
    }

    private static string GetGroupKey(GroupedMessage message)
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