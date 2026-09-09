using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

/// <summary>
/// The schemas already claimed on one <see cref="IServiceCollection"/>, so that a second
/// <c>DbContext</c> registered against a schema someone else took is refused rather than silently
/// sharing a table pair.
/// </summary>
/// <remarks>
/// In memory and at registration only. Nothing here consults the database: a startup query would fail
/// every host that migrates its own database after the host starts. See ADR 0007.
/// </remarks>
internal sealed class SchemaRegistry
{
    private readonly Dictionary<string, Type> _claims = new(StringComparer.Ordinal);

    /// <summary>
    /// Records that <paramref name="contextType"/> owns <paramref name="schema"/>. Claiming it twice for
    /// the same context is the ordinary inbox-and-outbox case and does nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">Another context already claimed the schema.</exception>
    internal void Claim(string schema, Type contextType)
    {
        if (_claims.TryGetValue(schema, out var owner))
        {
            if (owner == contextType)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Schema '{schema}' is already registered for {owner.Name}, so registering it for "
                + $"{contextType.Name} would give both contexts the same inbox and outbox tables. "
                + "Give each context its own schema.");
        }

        _claims.Add(schema, contextType);
    }

    /// <summary>
    /// The registry this service collection carries, created and registered on first use so that every
    /// registration call on one collection sees the same claims.
    /// </summary>
    internal static SchemaRegistry For(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SchemaRegistry) && descriptor.ImplementationInstance is SchemaRegistry existing)
            {
                return existing;
            }
        }

        var registry = new SchemaRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
