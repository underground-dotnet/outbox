using System.Collections.Concurrent;

namespace Underground.Inbox;

public class InMemoryInbox : IInbox
{
    private readonly ConcurrentQueue<InboxMessage> _messages = [];
    private Int32 _isProcessing;

    public Task AddAsync(InboxMessage message)
    {
        _messages.Enqueue(message);

        return Task.CompletedTask;
    }

    public async Task<bool> GetNextMessageAsync(Func<InboxMessage, Task<bool>> handler)
    {
        if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 1)
        {
            // already processing
            return false;
        }

        var messageFound = false;
        if (_messages.TryPeek(out var next))
        {
            messageFound = true;
            var success = await handler(next);

            if (success)
            {
                // remove message
                _messages.TryDequeue(out _);
            }
        }

        Interlocked.Exchange(ref _isProcessing, 0);
        return messageFound;
    }
}
