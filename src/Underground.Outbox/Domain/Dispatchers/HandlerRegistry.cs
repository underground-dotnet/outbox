using System.Diagnostics.CodeAnalysis;

using Underground.Outbox.Data;
using Underground.Outbox.Exceptions;

namespace Underground.Outbox.Domain.Dispatchers;

/// <summary>
/// The map from a message's stored <see cref="IMessage.Type"/> to the one handler that may run it,
/// built from the entries every module contributed.
/// </summary>
/// <remarks>
/// Building it is where two modules claiming one message type is caught. Entries arrive from the
/// container rather than from a single compilation, so the order in which the generated registration
/// methods were called does not matter.
/// </remarks>
/// <typeparam name="TEntity">The message table this registry covers.</typeparam>
public sealed class HandlerRegistry<TEntity> where TEntity : class, IMessage
{
    private readonly Dictionary<string, HandlerEntry<TEntity>> _byMessageTypeName;

    /// <summary>Builds the registry.</summary>
    /// <param name="entries">Every entry contributed to the container.</param>
    /// <exception cref="CompetingHandlersException">Two different handlers claim one message type.</exception>
    public HandlerRegistry(IEnumerable<HandlerEntry<TEntity>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _byMessageTypeName = [];

        foreach (var entry in entries)
        {
            if (!_byMessageTypeName.TryGetValue(entry.MessageTypeName, out var existing))
            {
                _byMessageTypeName.Add(entry.MessageTypeName, entry);
                continue;
            }

            // the same module registered twice is a no-op; two different handlers is the contradiction
            if (existing.HandlerType == entry.HandlerType)
            {
                continue;
            }

            throw new CompetingHandlersException(entry.MessageTypeName, existing.HandlerType, entry.HandlerType);
        }
    }

    /// <summary>Finds the entry for a stored message type.</summary>
    /// <param name="messageTypeName">The message's <see cref="IMessage.Type"/>.</param>
    /// <param name="entry">The entry, when one is registered.</param>
    /// <returns><see langword="true"/> when a handler claims this message type.</returns>
    public bool TryGetEntry(string messageTypeName, [NotNullWhen(true)] out HandlerEntry<TEntity>? entry)
        => _byMessageTypeName.TryGetValue(messageTypeName, out entry);
}
