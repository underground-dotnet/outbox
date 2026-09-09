using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Underground.Outbox.Domain;

/// <summary>
/// The one retention loop, covering every registered inbox and outbox. Each side keeps its own
/// <c>CleanupDelaySeconds</c> and is swept when its own delay has elapsed; the loop ticks at the shortest
/// of them, so retention does not cost a background loop per module.
/// </summary>
internal sealed partial class CleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IRetentionSweep> sweeps,
    ILogger<CleanupBackgroundService> logger
) : BackgroundService
{
    private readonly IReadOnlyList<IRetentionSweep> _sweeps = [.. sweeps];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sweeps.Count == 0)
        {
            return;
        }

        // A monotonic clock, and time left rather than an instant to reach: a timer that fires a shade early
        // leaves a small remainder for the next wait instead of skipping a whole period, and a sweep that
        // takes a while to delete does not push its own next run out.
        var elapsed = Stopwatch.StartNew();
        var lastTick = TimeSpan.Zero;
        var remaining = _sweeps.ToDictionary(sweep => sweep, sweep => sweep.Delay);

        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = remaining.Values.Min();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
            }

            var now = elapsed.Elapsed;
            var sinceLastTick = now - lastTick;
            lastTick = now;

            foreach (var sweep in _sweeps)
            {
                var left = remaining[sweep] - sinceLastTick;
                if (left > TimeSpan.Zero)
                {
                    remaining[sweep] = left;
                    continue;
                }

                remaining[sweep] = sweep.Delay;
                await PerformDeleteAsync(sweep, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task PerformDeleteAsync(IRetentionSweep sweep, CancellationToken stoppingToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var deletedCount = await sweep.ExecuteAsync(scope.ServiceProvider, stoppingToken).ConfigureAwait(false);

                LogDeletedMessages(deletedCount, sweep.Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCleanupFailed(sweep.Name, ex);
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Deleted {DeletedCount} processed messages from the {Side}.")]
    private partial void LogDeletedMessages(int deletedCount, string side);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Cleanup failed for the {Side}.")]
    private partial void LogCleanupFailed(string side, Exception exception);
}
