using System.Diagnostics;

using Underground.Outbox;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.Middleware;

namespace Underground.OutboxTest.Domain.Middleware;

/// <summary>
/// The span one message produces, without a database. Every assertion selects the activity by the
/// message's EventId, so a pipeline running in a parallel test cannot be mistaken for this one's.
/// </summary>
public class TraceMessageMiddlewareTests
{
    private const string ParentTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public async Task SpanContinuesTheTraceOfTheTransactionThatWroteTheMessage()
    {
        var (activity, _) = await ProcessAsync(traceParent: ParentTraceParent);

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", activity.TraceId.ToHexString());
        Assert.Equal("b7ad6b7169203331", activity.ParentSpanId.ToHexString());
    }

    [Fact]
    public async Task MessageWrittenWithNothingTracingStartsATraceOfItsOwn()
    {
        var (activity, _) = await ProcessAsync(traceParent: null);

        Assert.Equal(default, activity.ParentSpanId);
    }

    [Fact]
    public async Task UnparseableTraceContextStartsATraceOfItsOwnRatherThanThrowing()
    {
        var (activity, attempt) = await ProcessAsync(traceParent: "not-a-traceparent");

        Assert.Equal(default, activity.ParentSpanId);
        Assert.Equal(ProcessingStatus.Succeeded, attempt.Status);
    }

    [Fact]
    public async Task SpanCarriesTheMessagingAttributes()
    {
        var eventId = Guid.NewGuid();
        var message = NewMessage(eventId, traceParent: null);

        var (activity, _) = await ProcessAsync(message, () => Task.FromResult(ProcessingAttempt.Succeeded));

        Assert.Equal("process outbox", activity.DisplayName);
        Assert.Equal(ActivityKind.Consumer, activity.Kind);
        Assert.Equal(OutboxTelemetry.MessagingSystem, activity.GetTagItem("messaging.system"));
        Assert.Equal("process", activity.GetTagItem("messaging.operation.name"));
        Assert.Equal("process", activity.GetTagItem("messaging.operation.type"));
        Assert.Equal("outbox", activity.GetTagItem("messaging.destination.name"));
        Assert.Equal(eventId, activity.GetTagItem("messaging.message.id"));
        // the Group, under the name the semantic conventions give it
        Assert.Equal("orders", activity.GetTagItem("messaging.destination.partition.id"));
        Assert.Equal("Test.Message", activity.GetTagItem("underground.outbox.message.type"));
        Assert.Equal(3, activity.GetTagItem("underground.outbox.retry_count"));
    }

    [Fact]
    public async Task HandledMessageLeavesTheStatusUnset()
    {
        var (activity, _) = await ProcessAsync(traceParent: null);

        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        Assert.Null(activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task RecordedFailureIsAnErrorNamingTheException()
    {
        var failure = new InvalidOperationException("handler said no");

        var (activity, _) = await ProcessAsync(
            NewMessage(Guid.NewGuid(), traceParent: null),
            () => Task.FromResult(ProcessingAttempt.Failed(failure)));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
        Assert.Contains(activity.Events, e => string.Equals(e.Name, "exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeaseLostAfterAFailureIsReportedAsALostLease()
    {
        var (activity, _) = await ProcessAsync(
            NewMessage(Guid.NewGuid(), traceParent: null),
            () => Task.FromResult(ProcessingAttempt.LeaseLost(new InvalidOperationException())));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(TraceMessageMiddleware<OutboxMessage>.LeaseLostErrorType, activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task LeaseLostAfterTheHandlerSucceededIsReportedAsADuplicateDelivery()
    {
        // the effect was carried out and the completion write came too late, so it will be carried out again
        var (activity, _) = await ProcessAsync(
            NewMessage(Guid.NewGuid(), traceParent: null),
            () => Task.FromResult(ProcessingAttempt.LeaseLost(failure: null)));

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(TraceMessageMiddleware<OutboxMessage>.DuplicateDeliveryErrorType, activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task ShutdownMidAttemptIsNotAnError()
    {
        var eventId = Guid.NewGuid();
        var message = NewMessage(eventId, traceParent: null);
        var middleware = new TraceMessageMiddleware<OutboxMessage>();

        using var recorder = new ActivityRecorder(eventId);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => middleware.ExecuteAsync(message, scope: null!, _ => throw new OperationCanceledException(), TestContext.Current.CancellationToken));

        Assert.Equal(ActivityStatusCode.Unset, recorder.Single().Status);
    }

    private static async Task<(Activity Activity, ProcessingAttempt Attempt)> ProcessAsync(string? traceParent)
        => await ProcessAsync(NewMessage(Guid.NewGuid(), traceParent), () => Task.FromResult(ProcessingAttempt.Succeeded));

    private static async Task<(Activity Activity, ProcessingAttempt Attempt)> ProcessAsync(
        OutboxMessage message,
        Func<Task<ProcessingAttempt>> next)
    {
        var middleware = new TraceMessageMiddleware<OutboxMessage>();

        using var recorder = new ActivityRecorder(message.EventId);

        var attempt = await middleware.ExecuteAsync(message, scope: null!, _ => next(), TestContext.Current.CancellationToken);

        return (recorder.Single(), attempt);
    }

    private static OutboxMessage NewMessage(Guid eventId, string? traceParent) =>
        new(eventId, DateTime.UtcNow, "Test.Message", "{}", groupKey: "orders")
        {
            RetryCount = 3,
            TraceParent = traceParent,
        };

    /// <summary>
    /// Collects the spans this library emits for one message. The EventId filter is what keeps a pipeline
    /// running in a parallel test out of the result: an ActivityListener is process-wide.
    /// </summary>
    private sealed class ActivityRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly List<Activity> _activities = [];
        private readonly Guid _eventId;

        public ActivityRecorder(Guid eventId)
        {
            _eventId = eventId;
            _listener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, OutboxTelemetry.ActivitySourceName, StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    lock (_activities)
                    {
                        _activities.Add(activity);
                    }
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public Activity Single()
        {
            lock (_activities)
            {
                return _activities.Single(a => Equals(a.GetTagItem("messaging.message.id"), _eventId));
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
