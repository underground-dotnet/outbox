namespace Underground.Outbox.Domain;

/// <summary>
/// The workers of one registered inbox or outbox, seen without its type parameters so that one hosted
/// service can start every one of them.
/// </summary>
internal interface IWorkerSet
{
    /// <summary>Names this inbox or outbox in logs, as <c>OrdersContext outbox</c>.</summary>
    string Name { get; }

    /// <summary>Runs this side's workers until cancelled.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
