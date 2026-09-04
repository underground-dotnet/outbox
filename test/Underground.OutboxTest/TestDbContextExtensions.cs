using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox;
using Underground.Outbox.Data;

namespace Underground.OutboxTest;

/// <summary>
/// Reads and writes the outbox table's visibility column directly, so that tests can observe and move
/// it without going through the library. Shared because every test of a timing rule needs the same two
/// operations, whichever rule put the instant into the future.
/// </summary>
public static class TestDbContextExtensions
{
    extension(TestDbContext context)
    {
        /// <summary>
        /// How long the message still has to wait, measured by the database's own clock. Negative means it
        /// may be handled now.
        /// </summary>
        internal async Task<double> SecondsUntilVisibleAsync(long messageId, CancellationToken cancellationToken)
        {
            return await context.Database
                .SqlQuery<double>($"""SELECT extract(epoch FROM (visible_at - clock_timestamp()))::double precision AS "Value" FROM public.outbox WHERE id = {messageId}""")
                .SingleAsync(cancellationToken);
        }

        /// <summary>
        /// Writes messages through <see cref="IOutbox"/> inside a caller transaction, which is the only
        /// way the library accepts them. Shared because every test that arranges rows needs exactly this.
        /// </summary>
        internal async Task AddMessagesAsync(IServiceProvider serviceProvider, IEnumerable<OutboxMessage> messages, CancellationToken cancellationToken)
        {
            var outbox = serviceProvider.GetRequiredService<IOutbox>();

            var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await using (transaction.ConfigureAwait(false))
            {
                await outbox.AddMessagesAsync(context, messages, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Brings every unhandled message's visibility instant forward to now. All timing is decided by the
        /// database, so this is indistinguishable from having waited for that instant to arrive - whether it
        /// was a retry backoff or a scheduled delivery that put it into the future.
        /// </summary>
        internal async Task MakeUnhandledMessagesVisibleAsync(CancellationToken cancellationToken)
        {
            await context.Database.ExecuteSqlAsync(
                $"UPDATE public.outbox SET visible_at = clock_timestamp() WHERE processed_at IS NULL",
                cancellationToken);
        }
    }
}
