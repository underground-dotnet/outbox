using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.ExceptionHandlers;

/// <summary>
/// Which exception policy, if any, answers a Handler that threw. Built once from configuration, so the
/// rules for choosing between policies live here and nowhere else.
/// </summary>
internal sealed class ExceptionPolicyTable<TEntity> where TEntity : class, IMessage
{
    private readonly Dictionary<(HandlerType Handler, MessageType Message), List<ExceptionPolicy<TEntity>>> _handlerPolicies;
    private readonly List<ExceptionPolicy<TEntity>> _globalPolicies;

    /// <summary>
    /// Builds the table.
    /// </summary>
    /// <param name="registrations">The policies attached to (Handler, message type) pairs, in registration order.</param>
    /// <param name="globalPolicies">The policies that apply to every Handler, in registration order.</param>
    internal ExceptionPolicyTable(IEnumerable<HandlerRegistration<TEntity>> registrations, IEnumerable<ExceptionPolicy<TEntity>> globalPolicies)
    {
        _handlerPolicies = registrations
            .GroupBy(r => (r.HandlerType, r.MessageType))
            .ToDictionary(g => g.Key, g => g.SelectMany(r => r.ExceptionPolicies).ToList());
        _globalPolicies = [.. globalPolicies];
    }

    /// <summary>
    /// The one policy to run for <paramref name="thrown"/>, or <c>null</c> when none matches.
    /// </summary>
    internal ExceptionPolicy<TEntity>? Select(HandlerType handler, MessageType messageType, Exception thrown)
    {
        ArgumentNullException.ThrowIfNull(thrown);

        // A policy on the Handler wins over any global policy, however broad its exception type, so one
        // ForHandler chain reads as a complete override.
        var handlerPolicy = _handlerPolicies.TryGetValue((handler, messageType), out var policies)
            ? SelectNearest(policies, thrown.GetType())
            : null;

        return handlerPolicy ?? SelectNearest(_globalPolicies, thrown.GetType());
    }

    // within a level the nearest matching exception type wins, like a catch block
    private static ExceptionPolicy<TEntity>? SelectNearest(List<ExceptionPolicy<TEntity>> policies, Type thrownType)
    {
        for (var candidate = thrownType; candidate is not null; candidate = candidate.BaseType)
        {
            // the same exception type registered twice at one level keeps the first registration
            var match = policies.Find(p => p.ExceptionType == candidate);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
