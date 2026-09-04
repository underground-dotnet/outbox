using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

[Table("inbox")]
[Index(nameof(EventId), IsUnique = true)]
[EntityTypeConfiguration(typeof(InboxMessageConfiguration))]
public class InboxMessage : IMessage
{
    [Column("id")]
    [Key]
    public long Id { get; init; }

    [Column("event_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid EventId { get; init; }

    [Column("transaction_id")]
    public ulong TransactionId { get; init; }

    [Column("created_at")]
    public DateTime CreatedAt { get; init; }

    [Column("type")]
    public string Type { get; init; }

    [Column("group_key")]
    public string GroupKey { get; init; }

    [Column("data")]
    public string Data { get; init; }

    [Column("retry_count")]
    public int RetryCount { get; set; } = 0;

    [Column("visible_at")]
    public DateTime VisibleAt { get; init; }

    [Column(MessageColumns.ProcessedAt)]
    public DateTime? ProcessedAt { get; set; }

    internal InboxMessage(long id, Guid eventId, ulong transactionId, DateTime createdAt, string type, string groupKey, string data, int retryCount, DateTime visibleAt, DateTime? processedAt)
    {
        Id = id;
        EventId = eventId;
        TransactionId = transactionId;
        CreatedAt = createdAt;
        Type = type;
        GroupKey = groupKey;
        Data = data;
        RetryCount = retryCount;
        VisibleAt = visibleAt;
        ProcessedAt = processedAt;
    }

    /// <summary>
    /// Creates a message whose body is already serialized.
    /// </summary>
    /// <param name="eventId">Identifies the message. A second message with the same value is rejected.</param>
    /// <param name="createdAt">When the message was created, in UTC.</param>
    /// <param name="type">The name of the message type, which selects the handler.</param>
    /// <param name="data">The serialized message body.</param>
    /// <param name="groupKey">
    /// The Group this message belongs to. Messages of one Group are handled one at a time, in order.
    /// </param>
    /// <param name="visibleAt">
    /// The earliest instant, in UTC, at which this message may be handled. Omitted, or
    /// <see langword="null"/>, means as soon as possible: the database records the present, and the
    /// message is handled once it is Settled.
    /// <para>
    /// Scheduling a message also delays every message added to its Group after it. A Group offers only
    /// its Head - its oldest Settled message not yet handled - so nothing written behind a scheduled
    /// message is handled until the scheduled message has been. Give a message its own
    /// <paramref name="groupKey"/> if the delay is meant to apply to it alone.
    /// </para>
    /// </param>
    public InboxMessage(Guid eventId, DateTime createdAt, string type, string data, string groupKey = "default", DateTime? visibleAt = null)
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = type;
        GroupKey = groupKey;
        Data = data;
        VisibleAt = visibleAt.GetValueOrDefault();
    }

    /// <summary>
    /// Creates a message whose body is serialized to JSON, and whose type is taken from
    /// <paramref name="data"/>.
    /// </summary>
    /// <param name="eventId">Identifies the message. A second message with the same value is rejected.</param>
    /// <param name="createdAt">When the message was created, in UTC.</param>
    /// <param name="data">The message body.</param>
    /// <param name="groupKey">
    /// The Group this message belongs to. Messages of one Group are handled one at a time, in order.
    /// </param>
    /// <param name="visibleAt">
    /// The earliest instant, in UTC, at which this message may be handled. Omitted, or
    /// <see langword="null"/>, means as soon as possible: the database records the present, and the
    /// message is handled once it is Settled.
    /// <para>
    /// Scheduling a message also delays every message added to its Group after it. A Group offers only
    /// its Head - its oldest Settled message not yet handled - so nothing written behind a scheduled
    /// message is handled until the scheduled message has been. Give a message its own
    /// <paramref name="groupKey"/> if the delay is meant to apply to it alone.
    /// </para>
    /// </param>
    public InboxMessage(Guid eventId, DateTime createdAt, object data, string groupKey = "default", DateTime? visibleAt = null)
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = data.GetType().FullName!;
        GroupKey = groupKey;
        // TODO: move to AOT safe approach:
        // https://the-runtime.dev/articles/json-source-generator-system-text-json/
        // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
        Data = JsonSerializer.Serialize(data);
        VisibleAt = visibleAt.GetValueOrDefault();
    }
}
