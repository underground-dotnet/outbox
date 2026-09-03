using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Underground.Outbox.Data;

/// <summary>
/// The SQL identifiers of one message table, read off the EF model and quoted ready for
/// interpolation into a statement. A consumer may remap the table, its schema and every column, so
/// raw SQL cannot name them literally; resolving them in one place is what keeps the several
/// statements that do use raw SQL from each getting that wrong differently.
/// </summary>
internal sealed class MessageTable
{
    /// <summary>The schema-qualified table name.</summary>
    internal required string Name { get; init; }

    internal required string Id { get; init; }
    internal required string EventId { get; init; }
    internal required string TransactionId { get; init; }
    internal required string CreatedAt { get; init; }
    internal required string Type { get; init; }
    internal required string GroupKey { get; init; }
    internal required string Data { get; init; }
    internal required string RetryCount { get; init; }
    internal required string VisibleAt { get; init; }
    internal required string ProcessedAt { get; init; }

    /// <summary>
    /// Reads the identifiers of the table <typeparamref name="TEntity"/> is mapped to. The result
    /// depends only on the model, so a caller may cache it against the model it was built from.
    /// </summary>
    internal static MessageTable For<TEntity>(IModel model) where TEntity : class, IMessage
    {
        var entityType = model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Entity type {typeof(TEntity)} not found in DbContext model.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"Table name for entity type {typeof(TEntity)} is not configured.");
        var schema = entityType.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);

        return new MessageTable
        {
            Name = string.IsNullOrEmpty(schema) ? Quote(tableName) : $"{Quote(schema)}.{Quote(tableName)}",
            Id = Column(entityType, table, nameof(IMessage.Id)),
            EventId = Column(entityType, table, nameof(IMessage.EventId)),
            TransactionId = Column(entityType, table, nameof(IMessage.TransactionId)),
            CreatedAt = Column(entityType, table, nameof(IMessage.CreatedAt)),
            Type = Column(entityType, table, nameof(IMessage.Type)),
            GroupKey = Column(entityType, table, nameof(IMessage.GroupKey)),
            Data = Column(entityType, table, nameof(IMessage.Data)),
            RetryCount = Column(entityType, table, nameof(IMessage.RetryCount)),
            VisibleAt = Column(entityType, table, nameof(IMessage.VisibleAt)),
            ProcessedAt = Column(entityType, table, nameof(IMessage.ProcessedAt)),
        };
    }

    private static string Column(IEntityType entityType, StoreObjectIdentifier table, string propertyName)
    {
        var column = entityType.FindProperty(propertyName)?.GetColumnName(table)
            ?? throw new InvalidOperationException($"Property {propertyName} not found in entity type {entityType.ClrType}.");

        return Quote(column);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
