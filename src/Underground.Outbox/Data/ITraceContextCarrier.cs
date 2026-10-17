namespace Underground.Outbox.Data;

/// <summary>
/// A message that carries the OpenTelemetry trace context of the transaction that wrote it, taken from
/// <see cref="System.Diagnostics.Activity.Current"/>. Lets the add path set it on either message type without a
/// public setter.
/// </summary>
internal interface ITraceContextCarrier
{
    void SetTraceParent(string? traceParent);
}
