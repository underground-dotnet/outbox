using System.Threading.Channels;

namespace Underground.Outbox.Domain;

/// <summary>
/// The wake-up mechanism behind the worker pool: idle workers wait on it, and anything that knows work
/// may have appeared notifies it. It carries no information beyond "look again", and no guarantee that
/// looking will find anything.
/// </summary>
/// <remarks>
/// <para>
/// Two details of the implementation are load-bearing rather than incidental, and both read as mistakes
/// to someone who does not know what they are for.
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
/// nothing the first did not. Dropping it loses nothing either, because a token is only pending while
/// some worker's next claim has yet to start, and that claim sees whatever the dropped notification was
/// reporting.
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
    /// Waits until <see cref="Notify"/> is called. Returns rather than throwing when
    /// <paramref name="cancellationToken"/> is cancelled; the caller's loop decides what a cancellation
    /// means. Nothing here gives up on its own - a wait ends because somebody notified, or because the
    /// application is shutting down.
    /// </summary>
    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);

            // take the token so that the next wait blocks again
            _channel.Reader.TryRead(out _);
        }
        catch (OperationCanceledException)
        {
            // the application is shutting down, which the caller sees on its own token
        }
    }
}
