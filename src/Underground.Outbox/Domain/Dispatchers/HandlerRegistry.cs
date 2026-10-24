using System.Diagnostics.CodeAnalysis;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Dispatchers;

/// <summary>
/// The map from a message to the one Handler that may run it, built from the entries every module contributed.
/// </summary>
/// <remarks>
/// On the outbox a message's stored <see cref="IMessage.Type"/> selects the Handler; on the inbox a type may have
/// several Handlers, and the message's Recipient selects one. Building it is where two modules claiming one outbox message type is caught. Entries arrive from the
/// container rather than from a single compilation, so the order in which the generated registration
/// methods were called does not matter.
/// </remarks>
/// <typeparam name="TEntity">The message table this registry covers.</typeparam>
public sealed class HandlerRegistry<TEntity> where TEntity : class, IMessage
{
    private static readonly bool IsAddressed = typeof(TEntity).IsAssignableTo(typeof(IAddressedMessage));

    private readonly Dictionary<string, List<HandlerEntry<TEntity>>> _byMessageTypeName;

    /// <summary>Builds the registry.</summary>
    /// <param name="entries">Every entry contributed to the container.</param>
    /// <exception cref="CompetingHandlersException">Two different handlers claim one outbox message type.</exception>
    public HandlerRegistry(IEnumerable<HandlerEntry<TEntity>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _byMessageTypeName = [];

        foreach (var entry in entries)
        {
            if (!_byMessageTypeName.TryGetValue(entry.MessageTypeName, out var handlers))
            {
                _byMessageTypeName.Add(entry.MessageTypeName, [entry]);
                continue;
            }

            // the same module registered twice is a no-op
            if (handlers.Exists(e => e.HandlerType == entry.HandlerType))
            {
                continue;
            }

            // without a Recipient to tell them apart, two different handlers is the contradiction
            if (!IsAddressed)
            {
                throw new CompetingHandlersException(entry.MessageTypeName, handlers[0].HandlerType, entry.HandlerType);
            }

            handlers.Add(entry);
        }
    }

    /// <summary>Finds the one entry for a stored outbox message type.</summary>
    /// <param name="messageTypeName">The message's <see cref="IMessage.Type"/>.</param>
    /// <param name="entry">The entry, when exactly one Handler claims this type.</param>
    /// <returns>
    /// <see langword="true"/> when exactly one Handler claims this message type. An inbox type with several
    /// Handlers has none that is the one; look it up by Recipient instead.
    /// </returns>
    public bool TryGetEntry(string messageTypeName, [NotNullWhen(true)] out HandlerEntry<TEntity>? entry)
    {
        entry = _byMessageTypeName.TryGetValue(messageTypeName, out var handlers) && handlers.Count == 1
            ? handlers[0]
            : null;

        return entry is not null;
    }

    /// <summary>Finds the entry of the Handler an inbox message is addressed to.</summary>
    /// <param name="messageTypeName">The message's <see cref="IMessage.Type"/>.</param>
    /// <param name="recipient">The message's Recipient: its Handler's <see cref="System.Type.FullName"/>.</param>
    /// <param name="entry">The entry, when that Handler claims this message type.</param>
    /// <returns><see langword="true"/> when the Recipient is a registered Handler of this message type.</returns>
    internal bool TryGetEntry(string messageTypeName, string recipient, [NotNullWhen(true)] out HandlerEntry<TEntity>? entry)
    {
        entry = _byMessageTypeName.TryGetValue(messageTypeName, out var handlers)
            ? handlers.Find(e => string.Equals(e.Recipient, recipient, StringComparison.Ordinal))
            : null;

        return entry is not null;
    }

    /// <summary>The entry that runs <paramref name="message"/>: by its Recipient if it has one, else by its type.</summary>
    internal bool TryGetEntry(TEntity message, [NotNullWhen(true)] out HandlerEntry<TEntity>? entry)
        => message is IAddressedMessage addressed
            ? TryGetEntry(message.Type, addressed.Recipient, out entry)
            : TryGetEntry(message.Type, out entry);

    /// <summary>Every Handler of a message type, in the order their entries arrived; empty when none claims it.</summary>
    internal IReadOnlyList<HandlerEntry<TEntity>> EntriesFor(string messageTypeName)
        => _byMessageTypeName.TryGetValue(messageTypeName, out var handlers) ? handlers : [];
}
