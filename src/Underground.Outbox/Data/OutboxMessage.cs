using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

// TODO: how to use it from config file? and/or allow it for manual migrations with ef core
[Table("outbox")]
// TODO: or use EventId + partition?
[Index(nameof(EventId), IsUnique = true)]
public class OutboxMessage
{
    // TODO: or can we pass use snake case naming somehow to the connection?
    [Column("id")]
    [Key]
    public int Id { get; init; }

    [Column("event_id")]
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

    public OutboxMessage(Guid eventId, DateTime createdAt, string type, string data, string partitionKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = type;
        PartitionKey = partitionKey;
        Data = data;
    }

    public OutboxMessage(Guid eventId, DateTime createdAt, object data, string partitionKey = "default")
    {
        EventId = eventId;
        CreatedAt = createdAt;
        Type = data.GetType().AssemblyQualifiedName!;
        PartitionKey = partitionKey;
        Data = JsonSerializer.Serialize(data);
    }
}
