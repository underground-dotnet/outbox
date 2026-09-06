using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Underground.Outbox.Data;

/// <summary>
/// Model configuration shared by both message tables: the database-assigned
/// <see cref="IMessage.TransactionId"/> and <see cref="IMessage.VisibleAt"/>, and the partial index that
/// serves HeadMessage lookup. Applied automatically through <see cref="EntityTypeConfigurationAttribute"/>.
/// </summary>
/// <typeparam name="TEntity">The message entity being configured.</typeparam>
internal abstract class MessageConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class, IMessage
{
    public void Configure(EntityTypeBuilder<TEntity> builder)
    {
        // Properties are addressed by name, not by lambda: a lambda over TEntity reaches them through
        // IMessage, where the [Column] annotations are not.

        // assigned by the database, because the value has to identify the transaction doing the insert
        builder.Property(nameof(IMessage.TransactionId))
            .HasColumnType("xid8")
            .HasDefaultValueSql("pg_current_xact_id()")
            .ValueGeneratedOnAdd();

        // The default makes an unscheduled message deliverable at once, off the database's clock rather
        // than the application's. clock_timestamp() rather than now(), which is frozen for the transaction
        // the inbox holds open. A caller who schedules a message supplies the instant instead.
        builder.Property(nameof(IMessage.VisibleAt))
            .HasDefaultValueSql("clock_timestamp()")
            .ValueGeneratedOnAdd();

        // Serves HeadMessage lookup already ordered, so no sort is needed. Partial so its cost is proportional
        // to the unprocessed rows rather than the processed ones kept for the retention period.
        // The filter names the column literally because an IEntityTypeConfiguration runs before the
        // mapping annotations are applied; safe only because the names are fixed (ADR 0005).
        builder.HasIndex(nameof(IMessage.GroupKey), nameof(IMessage.TransactionId), nameof(IMessage.Id))
            .HasFilter("\"completed_at\" IS NULL");
    }
}
