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

    public InboxMessage(Guid eventId, DateTime createdAt, string type, string data, string groupKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = type;
        GroupKey = groupKey;
        Data = data;
    }

    public InboxMessage(Guid eventId, DateTime createdAt, object data, string groupKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = data.GetType().FullName!;
        GroupKey = groupKey;
        // TODO: move to AOT safe approach:
        // https://the-runtime.dev/articles/json-source-generator-system-text-json/
        // https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation
        Data = JsonSerializer.Serialize(data);
    }
}
