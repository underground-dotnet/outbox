namespace Underground.Outbox.Data;

public interface IMessage
{
    public long Id { get; }
    public Guid EventId { get; init; }
    public string Type { get; }
    public string PartitionKey { get; }
    public string Data { get; }
    public int RetryCount { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
