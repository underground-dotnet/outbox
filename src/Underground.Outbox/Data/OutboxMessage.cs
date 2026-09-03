using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

[Table("outbox")]
[Index(nameof(EventId), IsUnique = true)]
[Index(nameof(ProcessedAt), nameof(GroupKey))]
public class OutboxMessage : IMessage
{
    [Column("id")]
    [Key]
    public long Id { get; init; }

    [Column("event_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid EventId { get; init; }

    [Column("created_at")]
    public DateTime CreatedAt { get; init; }

    [Column("type")]
    public string Type { get; init; }

    [Column("group_key")]
    public string GroupKey { get; init; }

    [Column("data", TypeName = "jsonb")]
    public string Data { get; init; }

    [Column("retry_count")]
    public int RetryCount { get; set; } = 0;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    internal OutboxMessage(long id, Guid eventId, DateTime createdAt, string type, string groupKey, string data, int retryCount, DateTime? processedAt)
    {
        Id = id;
        EventId = eventId;
        CreatedAt = createdAt;
        Type = type;
        GroupKey = groupKey;
        Data = data;
        RetryCount = retryCount;
        ProcessedAt = processedAt;
    }

    public OutboxMessage(Guid eventId, DateTime createdAt, string type, string data, string groupKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = type;
        GroupKey = groupKey;
        Data = data;
    }

    public OutboxMessage(Guid eventId, DateTime createdAt, object data, string groupKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = data.GetType().FullName!;
        GroupKey = groupKey;
        Data = JsonSerializer.Serialize(data);
    }
}
