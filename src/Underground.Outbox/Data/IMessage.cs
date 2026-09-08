namespace Underground.Outbox.Data;

public interface IMessage
{
    /// <summary>
    /// The name of the table this message type is stored in. Fixed rather than read off the EF model,
    /// because the raw statements name it literally; see
    /// <c>docs/adr/0005-fixed-table-and-column-names.md</c>.
    /// </summary>
    public static abstract string TableName { get; }

    public long Id { get; }
    public Guid EventId { get; init; }

    /// <summary>
    /// Identifier of the transaction that inserted this message, assigned by PostgreSQL. With
    /// <see cref="Id"/> it forms the sort key of a Group: <see cref="Id"/> alone reflects append order
    /// rather than transaction order, and so misorders messages from two concurrent transactions.
    /// </summary>
    public ulong TransactionId { get; }

    public DateTime CreatedAt { get; }

    /// <summary>
    /// Runtime name of the payload's CLR type, as <see cref="System.Type.FullName"/> spells it. This is
    /// what selects the handler, so it is a contract the rows outlive: renaming or moving a message class
    /// orphans the messages already stored under the old name.
    /// </summary>
    public string Type { get; }
    public string GroupKey { get; }
    public string Data { get; }
    public int RetryCount { get; set; }

    /// <summary>
    /// The instant from which this message may be handled. Defaults to the present, is moved into the
    /// future by the retry backoff after a failure, and may be set at creation to schedule the message.
    /// </summary>
    public DateTime VisibleAt { get; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The W3C <c>traceparent</c> of the transaction that wrote this message, so the worker that later
    /// handles it continues the same trace. Null when nothing was tracing at the time.
    /// </summary>
    public string? TraceParent { get; }
}
