using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

[Table("inbox")]
[Index(nameof(EventId), IsUnique = true)]
[Index(nameof(ProcessedAt), nameof(PartitionKey))]
public class InboxMessage : IMessage
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

    [Column("partition_key")]
    public string PartitionKey { get; init; }

    [Column("data")]
    public string Data { get; init; }

    [Column("retry_count")]
    public int RetryCount { get; set; } = 0;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    public InboxMessage(Guid eventId, DateTime createdAt, string type, string data, string partitionKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = type;
        PartitionKey = partitionKey;
        Data = data;
    }

    public InboxMessage(Guid eventId, DateTime createdAt, object data, string partitionKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = data.GetType().FullName!;
        PartitionKey = partitionKey;
        Data = JsonSerializer.Serialize(data);
    }
}
