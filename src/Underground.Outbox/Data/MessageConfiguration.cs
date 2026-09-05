using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Underground.Outbox.Data;

/// <summary>
/// Model configuration shared by both message tables: the database-assigned
/// <see cref="IMessage.TransactionId"/> and <see cref="IMessage.VisibleAt"/>, and the partial index
/// that serves Head lookup.
/// It is applied automatically through <see cref="EntityTypeConfigurationAttribute"/> on the entity
/// types, so a consumer's model carries it without a call they have to remember.
/// </summary>
/// <typeparam name="TEntity">The message entity being configured.</typeparam>
internal abstract class MessageConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class, IMessage
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        // Properties are addressed by name rather than through a lambda: a lambda over TEntity reaches
        // them through IMessage, and EF then identifies the property by the interface's member, where the
        // [Column] annotations are not.

        // xid8 is assigned by the database rather than the application, because the value has to identify
        // the transaction doing the insert. pg_current_xact_id() assigns the transaction one if it does
        // not have one yet.
        builder.Property(nameof(IMessage.TransactionId))
            .HasColumnType("xid8")
            .HasDefaultValueSql("pg_current_xact_id()")
            .ValueGeneratedOnAdd();

        // The default is what makes an unscheduled message deliverable at once, and leaving the column to
        // the database is what keeps that instant off the application's clock. clock_timestamp() rather
        // than now(), because now() is frozen for the transaction the inbox holds open across its handler.
        // A caller who schedules a message supplies the instant instead, and EF then includes the column
        // in the insert rather than letting the default apply.
        builder.Property(nameof(IMessage.VisibleAt))
            .HasDefaultValueSql("clock_timestamp()")
            .ValueGeneratedOnAdd();

        // Head lookup reads the lowest (TransactionId, Id) per GroupKey among unprocessed messages, which
        // this index supplies already ordered, so no sort is needed to pick the Heads out. Its cost is
        // proportional to the unprocessed rows rather than to the processed ones kept for the retention
        // period, which is what the filter buys and which is the whole reason the index is partial.
        // PostgreSQL has no loose index scan, so the DISTINCT ON still walks every unprocessed entry
        // rather than skipping from one GroupKey to the next.
        // The filter names the column literally because an IEntityTypeConfiguration runs before the
        // mapping annotations are applied and so cannot read the name back off the model. That is safe
        // only because the column names are fixed; see docs/adr/0005-fixed-table-and-column-names.md.
        builder.HasIndex(nameof(IMessage.GroupKey), nameof(IMessage.TransactionId), nameof(IMessage.Id))
            .HasFilter("\"processed_at\" IS NULL");
    }
}
