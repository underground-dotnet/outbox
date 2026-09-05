namespace Underground.Outbox.Data;

/// <summary>
/// Metadata about a message being processed.
/// </summary>
public record MessageMetadata(Guid EventId, string GroupKey, int RetryCount = 0);
