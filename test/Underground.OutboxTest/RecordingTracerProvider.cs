using System.Diagnostics;

using OpenTelemetry;
using OpenTelemetry.Trace;

using Underground.Outbox;

namespace Underground.OutboxTest;

/// <summary>
/// Collects the spans this library emits, through the same OpenTelemetry pipeline a consumer configures:
/// a real <see cref="TracerProvider"/> subscribed by source name, exporting into memory.
/// </summary>
/// <remarks>
/// A provider listens process-wide, so a pipeline running in a parallel test lands here too. That is what
/// <see cref="SpanFor"/> filters on the message's EventId for; nothing about the exporter removes the need.
/// </remarks>
public sealed class RecordingTracerProvider : IDisposable
{
    private readonly List<Activity> _exported = [];
    private readonly TracerProvider _provider;

    /// <summary>
    /// Records this library's spans, and those of any extra sources named — a test that needs a producer
    /// Activity to exist has to have its source subscribed too, or StartActivity returns null.
    /// </summary>
    public RecordingTracerProvider(params string[] alsoRecord)
    {
        ArgumentNullException.ThrowIfNull(alsoRecord);

        var builder = Sdk.CreateTracerProviderBuilder()
            .AddSource(OutboxTelemetry.ActivitySourceName)
            .AddInMemoryExporter(_exported);

        foreach (var source in alsoRecord)
        {
            builder.AddSource(source);
        }

        _provider = builder.Build();
    }

    /// <summary>The one span this library emitted for the given message.</summary>
    public Activity SpanFor(Guid eventId)
    {
        _provider.ForceFlush();

        return _exported.Single(activity => Equals(activity.GetTagItem("messaging.message.id"), eventId));
    }

    public void Dispose() => _provider.Dispose();
}
