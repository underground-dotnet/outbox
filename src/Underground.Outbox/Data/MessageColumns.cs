namespace Underground.Outbox.Data;

/// <summary>
/// Column names that are written twice: once as a mapping annotation on the entity, and once inside
/// raw SQL. Sharing the constant is what keeps the two from drifting apart.
/// </summary>
internal static class MessageColumns
{
    /// <summary>
    /// Named in the Head index's filter. The filter is raw SQL, and an
    /// <see cref="Microsoft.EntityFrameworkCore.IEntityTypeConfiguration{TEntity}"/> runs before the
    /// mapping annotations are applied, so the name cannot be read back off the model there.
    /// </summary>
    internal const string ProcessedAt = "processed_at";
}
