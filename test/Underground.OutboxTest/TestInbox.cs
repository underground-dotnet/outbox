using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Inboxes;

namespace Underground.OutboxTest;

/// <summary>
/// The Inbox this assembly's inbox handlers are bound to, and a way into it for tests that drive its
/// services directly rather than through its workers.
/// </summary>
internal static class TestInbox
{
    internal const string Name = "test";

    /// <summary>A scope that works for the named Inbox, as one of its workers would open.</summary>
    internal static AsyncServiceScope CreateInboxScope(this IServiceProvider provider, string inbox = Name) =>
        provider.GetRequiredService<InboxDirectory>()[inbox].ScopeFactory.CreateAsyncScope();

    /// <summary>The workers of the named Inbox.</summary>
    internal static ConcurrentProcessor<InboxMessage> InboxWorkers(this IServiceProvider provider, string inbox = Name) =>
        provider.GetRequiredService<InboxDirectory>()[inbox].Processor;
}
