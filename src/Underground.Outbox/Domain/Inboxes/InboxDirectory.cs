using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Inboxes;

/// <summary>
/// Every Inbox the host runs, and which of them handle a message type. Built as the host starts, which is
/// where a Handler bound to an Inbox nobody registered is caught.
/// </summary>
internal sealed class InboxDirectory
{
    private readonly Dictionary<string, Inbox> _byName;
    private readonly Dictionary<string, string[]> _inboxesByMessageType;

    public InboxDirectory(
        IEnumerable<InboxRegistration> registrations,
        IEnumerable<HandlerEntry<InboxMessage>> entries,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory)
    {
        var entriesByInbox = entries.ToLookup(entry => entry.Inbox, StringComparer.Ordinal);

        _byName = registrations.ToDictionary(
            registration => registration.Name,
            registration => new Inbox(registration, entriesByInbox[registration.Name], scopeFactory, loggerFactory),
            StringComparer.Ordinal);

        // staging would write such a message into an Inbox nobody claims from
        var unregistered = entriesByInbox.FirstOrDefault(group => group.Key is null || !_byName.ContainsKey(group.Key));
        if (unregistered is not null)
        {
            throw new UnknownInboxException(unregistered.Key, unregistered.First().HandlerType);
        }

        _inboxesByMessageType = entriesByInbox
            .SelectMany(group => group.Select(entry => (entry.MessageTypeName, Inbox: group.Key!)))
            .Distinct()
            .GroupBy(pair => pair.MessageTypeName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Inbox).ToArray(), StringComparer.Ordinal);
    }

    internal Inbox this[string name] => _byName[name];

    internal IEnumerable<Inbox> All => _byName.Values;

    /// <summary>The Inboxes with a Handler for a stored message type; empty when none has one.</summary>
    internal IReadOnlyList<string> Handling(string messageTypeName) =>
        _inboxesByMessageType.TryGetValue(messageTypeName, out var inboxes) ? inboxes : [];

    /// <summary>Wakes the workers of the named Inbox; a name no Inbox has is ignored.</summary>
    internal void NotifyWork(string inbox)
    {
        if (_byName.TryGetValue(inbox, out var running))
        {
            running.Processor.NotifyWork();
        }
    }
}
