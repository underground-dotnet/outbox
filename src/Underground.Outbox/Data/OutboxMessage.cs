using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Underground.Outbox.Data;

// TODO: how to use it from config file? and/or allow it for manual migrations with ef core
[Table("outbox")]
public class OutboxMessage
{
    // TODO: or can we pass use snake case naming somehow to the connection?
    // TODO: move to guid?
    [Column("id")]
    public int Id { get; init; }

    [Column("trace_id")]
    public Guid TraceId { get; init; }

    [Column("occurred_on")]
    public DateTime OccurredOn { get; init; }

    [Column("type")]
    public string Type { get; init; }

    [Column("partition_key")]
    public string PartitionKey { get; init; }

    [Column("data")]
    public string Data { get; init; }

    [Column("completed")]
    public bool Completed { get; set; } = false;

    public OutboxMessage(Guid traceId, DateTime occurredOn, string type, string data, string partitionKey = "default")
    {
        TraceId = traceId;
        OccurredOn = occurredOn;
        Type = type;
        PartitionKey = partitionKey;
        Data = data;
    }

    public OutboxMessage(Guid traceId, DateTime occurredOn, object data)
    {
        TraceId = traceId;
        OccurredOn = occurredOn;
        Type = data.GetType().AssemblyQualifiedName!;
        PartitionKey = "default";
        Data = JsonSerializer.Serialize(data);
    }
}
