namespace Underground.Outbox.Data;

public interface IMessage
{
    public long Id { get; }
    public Guid EventId { get; init; }

    /// <summary>
    /// Identifier of the transaction that inserted this message, assigned by PostgreSQL.
    /// Together with <see cref="Id"/> it is the sort key of a Group: <see cref="Id"/> alone reflects
    /// the order in which messages were appended rather than the order in which their transactions
    /// started, and so cannot order messages written by two concurrent transactions correctly.
    /// </summary>
    public ulong TransactionId { get; }

    public DateTime CreatedAt { get; }
    public string Type { get; }
    public string GroupKey { get; }
    public string Data { get; }
    public int RetryCount { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
