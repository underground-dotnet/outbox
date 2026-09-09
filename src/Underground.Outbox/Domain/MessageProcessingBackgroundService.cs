using Microsoft.Extensions.Hosting;

namespace Underground.Outbox.Domain;

/// <summary>
/// The one hosted service, which starts the workers of every registered inbox and outbox, so that starting
/// and stopping the application starts and stops all of them together.
/// </summary>
internal sealed class MessageProcessingBackgroundService(IEnumerable<IWorkerSet> workerSets) : BackgroundService
{
    private readonly IReadOnlyList<IWorkerSet> _workerSets = [.. workerSets];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_workerSets.Count == 0)
        {
            return;
        }

        var running = _workerSets.Select(set => set.RunAsync(stoppingToken)).ToList();

        // WhenAny first, so that one module's worker set faulting surfaces here now rather than being held
        // until shutdown - which is what WhenAll alone would do, leaving that module silently idle while the
        // host stayed up. A worker set only finishes on its own at shutdown, when the rest finish with it.
        await (await Task.WhenAny(running).ConfigureAwait(false)).ConfigureAwait(false);

        await Task.WhenAll(running).ConfigureAwait(false);
    }
}
