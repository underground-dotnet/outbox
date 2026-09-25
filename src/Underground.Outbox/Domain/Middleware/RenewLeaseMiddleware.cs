using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Keeps the Lease of an outbox Handler that is still running after its timeout. Cancelling a Handler only
/// asks it to stop, and one that ignores the token would otherwise outlive its Lease and run at the same
/// time as the worker that claims the message next. See ADR 0011.
/// </summary>
/// <remarks>
/// Idle until <see cref="ServiceConfiguration{TEntity}.HandlerTimeout"/> has passed. From then on it moves
/// the Lease expiry forward every <see cref="ServiceConfiguration{TEntity}.LeaseRenewalInterval"/>, guarded
/// on the current Lease, and hands the new instant to the outcome writes through
/// <see cref="OutboxMessage.VisibleAt"/>. It has to sit inside the outcome writes so they see the last
/// renewal, and outside <see cref="TimeoutMiddleware{TEntity}"/> so it outlasts the Handler.
/// </remarks>
internal sealed partial class RenewLeaseMiddleware(
    IServiceScopeFactory scopeFactory,
    ServiceConfiguration<OutboxMessage> config,
    ILogger<RenewLeaseMiddleware> logger
) : IMessageMiddleware<OutboxMessage>
{
    private readonly ILogger<RenewLeaseMiddleware> _logger = logger;

    public async Task<ProcessingAttempt> ExecuteAsync(OutboxMessage message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        // not linked to the caller's token: a shutdown does not stop a Handler that ignores it, so it must
        // not stop the renewal either
        using var handlerReturned = new CancellationTokenSource();
        var renewal = RenewWhileOverrunningAsync(message, handlerReturned.Token);

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await handlerReturned.CancelAsync().ConfigureAwait(false);
            await renewal.ConfigureAwait(false);
        }
    }

    private async Task RenewWhileOverrunningAsync(OutboxMessage message, CancellationToken handlerReturned)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            await Task.Delay(config.HandlerTimeout, handlerReturned).ConfigureAwait(false);

            // its own scope and connection, because the Handler may be using the scoped DbContext right now
            var renewalScope = scopeFactory.CreateAsyncScope();
            await using (renewalScope.ConfigureAwait(false))
            {
                var dbContext = renewalScope.ServiceProvider.GetRequiredService<IOutboxDbContext>();
                using var timer = new PeriodicTimer(config.LeaseRenewalInterval);

                while (await timer.WaitForNextTickAsync(handlerReturned).ConfigureAwait(false))
                {
                    if (!await TryRenewAsync(dbContext, message, Stopwatch.GetElapsedTime(started)).ConfigureAwait(false))
                    {
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (handlerReturned.IsCancellationRequested)
        {
            // the Handler returned; the outcome write releases the Lease from here
        }
    }

    /// <summary>
    /// Renews the Lease once, if this worker still holds it.
    /// </summary>
    /// <returns>Whether to keep renewing. <c>false</c> once the Lease is no longer this worker's.</returns>
    private async Task<bool> TryRenewAsync(IOutboxDbContext dbContext, OutboxMessage message, TimeSpan elapsed)
    {
        try
        {
            // Never cancelled: a renewal that landed but whose new instant was not read would leave the
            // outcome write guarded on a Lease that no longer exists.
            var renewed = await RenewAsync(dbContext, message, CancellationToken.None).ConfigureAwait(false);

            if (renewed is null)
            {
                LogRenewalLost(message.Id);
                return false;
            }

            message.VisibleAt = renewed.Value;
            LogRenewed(message.Id, elapsed);
        }
        // the Lease still has two intervals left, so a failed renewal is logged and tried again on the next tick
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRenewalFailed(ex, message.Id);
        }

        return true;
    }

    private async Task<DateTime?> RenewAsync(IOutboxDbContext dbContext, OutboxMessage message, CancellationToken cancellationToken)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var command = dbContext.Database.GetDbConnection().CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            // an interval rather than an instant, so a skewed application clock cannot expire the Lease early.
            // S2077: the only interpolated value is OutboxMessage.TableName, a compile-time constant (ADR 0005)
#pragma warning disable S2077 // Formatting SQL queries is security-sensitive
            command.CommandText = $"""
                UPDATE {OutboxMessage.TableName}
                SET visible_at = clock_timestamp() + @margin
                WHERE id = @id
                AND visible_at = @lease
                RETURNING visible_at
                """;
#pragma warning restore S2077
            command.Parameters.Add(new NpgsqlParameter("id", message.Id));
            command.Parameters.Add(new NpgsqlParameter("lease", message.VisibleAt));
            command.Parameters.Add(new NpgsqlParameter("margin", config.LeaseMargin));

            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as DateTime?;
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The Handler for message {MessageId} is still running {Elapsed} after it started, ignoring its cancellation; renewed its Lease so no other worker takes the message")]
    private partial void LogRenewed(long messageId, TimeSpan elapsed);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Could not renew the Lease on message {MessageId}: it is no longer this worker's")]
    private partial void LogRenewalLost(long messageId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Renewing the Lease on message {MessageId} failed; trying again on the next interval")]
    private partial void LogRenewalFailed(Exception exception, long messageId);
}
