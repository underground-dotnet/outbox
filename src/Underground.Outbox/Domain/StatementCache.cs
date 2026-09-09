using System.Collections.Concurrent;

namespace Underground.Outbox.Domain;

/// <summary>
/// The raw statements, composed once per schema-qualified table and kept against that string.
/// </summary>
/// <remarks>
/// Non-generic, so one cache serves every closed processor type. The key is a string written at
/// registration rather than an <c>IModel</c>, so none of the per-model machinery ADR 0005 removed comes
/// back with the schema of ADR 0007.
/// </remarks>
internal static class StatementCache
{
    private static readonly ConcurrentDictionary<(string QualifiedTable, string Kind), string> Statements = new();

    /// <summary>The statement for this table, composing it on first use.</summary>
    /// <param name="qualifiedTable">The table as the statement names it, schema included.</param>
    /// <param name="kind">Which statement this is, since several are composed for one table.</param>
    /// <param name="compose">Writes the statement for a table. Called at most once per table and kind.</param>
    internal static string GetOrAdd(string qualifiedTable, string kind, Func<string, string> compose)
    {
        return Statements.GetOrAdd((qualifiedTable, kind), key => compose(key.QualifiedTable));
    }
}
