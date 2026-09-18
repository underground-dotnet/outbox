using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Attributes;

/// <summary>
/// Sets the dependency-injection lifetime of a message handler. Optional: a handler without it is
/// registered as <see cref="ServiceLifetime.Transient"/>.
/// </summary>
/// <remarks>
/// It governs both inbox and outbox handlers, and it governs the handler class rather than one of the
/// message types it handles. A handler implementing several handler interfaces is registered once as
/// its concrete type at this lifetime, with each interface resolving that one registration - so
/// <see cref="ServiceLifetime.Scoped"/> means one instance per scope, not one per message type.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MessageHandlerLifetimeAttribute(ServiceLifetime lifetime) : Attribute
{
    /// <summary>The lifetime the handler is registered with.</summary>
    public ServiceLifetime Lifetime { get; } = lifetime;
}
