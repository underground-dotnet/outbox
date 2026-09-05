using System.Threading.Channels;

namespace Underground.Outbox.Domain;

/// <summary>
/// The wake-up mechanism behind the worker pool: idle workers wait on it, and anything that knows work
/// may have appeared notifies it. Polling is what actually guarantees delivery - a wait gives up after
/// the poll delay whether or not anyone notified - so this exists only to cut the latency between a
/// commit and the handling that follows it.
/// </summary>
/// <remarks>
/// <para>
/// Three details of the implementation are load-bearing rather than incidental, and all three read as
/// mistakes to someone who does not know what they are for.
/// </para>
/// <para>
/// <b>A notification releases every waiter, not one.</b> <see cref="WaitAsync"/> awaits
/// <c>WaitToReadAsync</c>, which completes for all waiters, and only then drains the token. Whichever
/// worker wins that race is immaterial, because they have all been released by the time it is drained.
/// Releasing one instead would leave a commit that arrives at an idle pool served by a single worker
/// handling every Group serially until the next poll.
/// </para>
/// <para>
/// <b>The notification is buffered, so it cannot be lost.</b> A <see cref="Notify"/> that lands between
/// a worker finding no work and that worker starting to wait leaves the token sitting in the channel,
/// and the wait returns immediately. A plain pulse would drop that notification and cost a full poll
/// delay. The channel is bounded at one with <see cref="BoundedChannelFullMode.DropWrite"/> because the
/// token carries no information: a second notification arriving before the first is consumed says
/// nothing the first did not.
/// </para>
/// <para>
/// <b>The timeout is a linked <see cref="CancellationTokenSource"/> rather than a raced
/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</b> Racing the two with
/// <see cref="Task.WhenAny(Task, Task)"/> would abandon the losing task on every wait, and an abandoned
/// <c>WaitToReadAsync</c> keeps its registration on the channel forever. Cancelling the wait is what
/// tears it down.
/// </para>
/// <para>
/// Notifying is in-process only: a commit on one application instance does not wake the workers of
/// another, which pick the work up on their next poll instead. Closing that gap means a
/// <c>LISTEN</c>/<c>pg_notify</c> subscription per instance, which would be a third caller of
/// <see cref="Notify"/> alongside the commit interceptor and the poll delay rather than a replacement
/// for either.
/// </para>
/// </remarks>
internal sealed class WorkSignal
{
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    /// <summary>
    /// Reports that work may have appeared, releasing everyone currently waiting. It never blocks and
    /// never fails: a notification that arrives while one is already pending is dropped, because the two
    /// say the same thing.
    /// </summary>
    internal void Notify()
    {
        _channel.Writer.TryWrite(0);
    }

    /// <summary>
    /// Waits until <see cref="Notify"/> is called, giving up after <paramref name="timeout"/> so that
    /// work nobody notified us about is still picked up. Returns rather than throwing when
    /// <paramref name="cancellationToken"/> is cancelled; the caller's loop decides what a cancellation
    /// means.
    /// </summary>
    internal async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(timeout);

        try
        {
            await _channel.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false);

            // take the token so that the next wait blocks again
            _channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException)
        {
            // either the poll delay elapsed, which is itself a reason to look for work, or the
            // application is shutting down, which the caller sees on its own token
        }
    }
}
