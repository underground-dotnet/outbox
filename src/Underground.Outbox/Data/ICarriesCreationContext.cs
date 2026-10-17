namespace Underground.Outbox.Data;

/// <summary>
/// Lets the add path stamp the Creation Context on either message type without a public setter.
/// </summary>
internal interface ICarriesCreationContext
{
    void StampCreationContext(string? traceParent);
}
