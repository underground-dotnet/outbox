namespace Underground.Outbox.Data;

/// <summary>
/// Names a message table qualified by the schema chosen at registration, so two modules sharing a
/// connection string reach two different table pairs. See <c>docs/adr/0007-schema-is-chosen-at-registration.md</c>.
/// </summary>
internal static class SchemaQualifiedTable
{
    /// <summary>
    /// <c>"orders".outbox</c>: the schema quoted so it survives casing and reserved words, the table
    /// name bare because it is a compile-time constant (ADR 0005).
    /// </summary>
    internal static string For<TEntity>(string schema) where TEntity : class, IMessage
        => $"\"{schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\".{TEntity.TableName}";
}
