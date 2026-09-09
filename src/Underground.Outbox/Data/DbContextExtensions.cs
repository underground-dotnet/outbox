using Microsoft.EntityFrameworkCore;

namespace Underground.Outbox.Data;

/// <summary>
/// Runs a unit of work in a transaction the caller does not have to own, under whatever Execution Strategy
/// the host configured.
/// </summary>
/// <remarks>
/// Adding to the inbox or the outbox requires an active transaction, and a host that configures a retrying
/// Execution Strategy - as Aspire's <c>EnrichNpgsqlDbContext</c> does - refuses a transaction the caller
/// began itself. Going through here is the same call in both cases. See ADR 0006.
/// </remarks>
public static class DbContextExtensions
{
    extension(DbContext context)
    {
        /// <summary>
        /// Runs <paramref name="work"/> in a transaction and commits it.
        /// </summary>
        /// <remarks>
        /// A retrying Execution Strategy may run <paramref name="work"/> more than once, so it must be
        /// re-runnable: stage everything it needs inside the delegate rather than before the call. When a
        /// transaction is already open, it is joined and retries belong to whoever opened it.
        /// </remarks>
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            return ExecuteAsync<object?>(context, async ct =>
            {
                await work(ct).ConfigureAwait(false);
                return null;
            }, cancellationToken);
        }

        /// <summary>
        /// Runs <paramref name="work"/> in a transaction, commits it, and returns what it produced.
        /// </summary>
        /// <remarks>
        /// A retrying Execution Strategy may run <paramref name="work"/> more than once, so it must be
        /// re-runnable: stage everything it needs inside the delegate rather than before the call. When a
        /// transaction is already open, it is joined and retries belong to whoever opened it.
        /// </remarks>
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(work);

            return ExecuteAsync(context, work, cancellationToken);
        }
    }

    private static async Task<TResult> ExecuteAsync<TResult>(DbContext context, Func<CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        // a nested call cannot own a boundary someone else already owns, and an Execution Strategy applied
        // here would retry an inner slice of an outer transaction
        if (context.Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken).ConfigureAwait(false);
        }

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var result = await work(ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return result;
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
