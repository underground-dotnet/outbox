using System;

namespace Underground;

public interface IInbox
{
    public Task AddAsync(InboxMessage message);
    public Task<bool> GetNextMessageAsync(Func<InboxMessage, Task<bool>> handler);
}
