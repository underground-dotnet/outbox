using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;

namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// Opens the OpenTelemetry "process" span for one message, continuing the trace of the transaction that
/// wrote it, and closes it with what became of the message.
/// </summary>
internal sealed class TraceMessageMiddleware<TEntity> : IMessageMiddleware<TEntity> where TEntity : class, IMessage
{
    /// <summary>
    /// A lost Lease after the Handler threw: the failure was never recorded, because the message is no
    /// longer this worker's.
    /// </summary>
    internal const string LeaseLostErrorType = "lease_lost";

    /// <summary>
    /// A lost Lease after the Handler succeeded: the effect has been carried out twice.
    /// </summary>
    internal const string DuplicateDeliveryErrorType = "duplicate_delivery";

    public async Task<ProcessingAttempt> ExecuteAsync(TEntity message, IServiceScope scope, MessageMiddlewareDelegate next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var activity = StartActivity(message);

        var attempt = await next(cancellationToken).ConfigureAwait(false);

        Record(activity, attempt);

        return attempt;
    }

    private static Activity? StartActivity(TEntity message)
    {
        // an unparseable value is a message that still has to be handled, so it starts a trace of its own
        // rather than throwing out of the outermost middleware and stalling the Group
        var parent = ActivityContext.TryParse(message.TraceParent, traceState: null, out var context)
            ? context
            : default;

        var activity = OutboxTelemetry.ActivitySource.StartActivity(
            $"process {TEntity.TableName}",
            ActivityKind.Consumer,
            parent);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag("messaging.system", OutboxTelemetry.MessagingSystem);
        activity.SetTag("messaging.operation.name", "process");
        activity.SetTag("messaging.operation.type", "process");
        activity.SetTag("messaging.destination.name", TEntity.TableName);
        activity.SetTag("messaging.message.id", message.EventId);

        // the Group, under the name the semantic conventions give it. CONTEXT.md reserves "Partition" for
        // PostgreSQL table partitioning; this attribute is the documented exception.
        activity.SetTag("messaging.destination.partition.id", message.GroupKey);

        activity.SetTag("underground.outbox.message.type", message.Type);
        activity.SetTag("underground.outbox.retry_count", message.RetryCount);

        return activity;
    }

    private static void Record(Activity? activity, ProcessingAttempt attempt)
    {
        if (activity is null)
        {
            return;
        }

        switch (attempt.Status)
        {
            case ProcessingStatus.Succeeded:
                // left Unset: Ok is reserved for an application asserting success explicitly
                break;

            case ProcessingStatus.FailureRecorded:
                SetError(activity, attempt.Failure!.GetType().FullName!, attempt.Failure);
                break;

            case ProcessingStatus.LeaseLost:
                SetError(
                    activity,
                    attempt.Failure is null ? DuplicateDeliveryErrorType : LeaseLostErrorType,
                    attempt.Failure);
                break;

            default:
                break;
        }
    }

    private static void SetError(Activity activity, string errorType, Exception? failure)
    {
        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag("error.type", errorType);

        if (failure is not null)
        {
            activity.AddException(failure);
        }
    }
}
