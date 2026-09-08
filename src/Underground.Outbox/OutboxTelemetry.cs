using System.Diagnostics;
using System.Reflection;

namespace Underground.Outbox;

/// <summary>
/// The <see cref="System.Diagnostics.ActivitySource"/> this library traces message Processing on.
/// </summary>
/// <remarks>
/// Only the BCL is used, so nothing is emitted unless a consumer subscribes: pass
/// <see cref="ActivitySourceName"/> to <c>AddSource</c> on an OpenTelemetry tracer provider.
/// </remarks>
public static class OutboxTelemetry
{
    /// <summary>
    /// The name to subscribe to, as <c>builder.AddSource(OutboxTelemetry.ActivitySourceName)</c>.
    /// </summary>
    public const string ActivitySourceName = "Underground.Outbox";

    /// <summary>
    /// The value emitted as <c>messaging.system</c>. One value for both sides: the system is this library,
    /// and the side is already <c>messaging.destination.name</c>.
    /// </summary>
    public const string MessagingSystem = "underground_outbox";

    internal static ActivitySource ActivitySource { get; } = new(
        ActivitySourceName,
        typeof(OutboxTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
