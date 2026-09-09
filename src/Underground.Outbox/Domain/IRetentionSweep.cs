namespace Underground.Outbox.Domain;

/// <summary>
/// The retention sweep of one registered inbox or outbox, seen without its type parameters so that one
/// cleanup loop covers every module rather than one loop per module.
/// </summary>
internal interface IRetentionSweep
{
    /// <summary>Names this inbox or outbox in logs, as <c>OrdersContext outbox</c>.</summary>
    string Name { get; }

    /// <summary>How long this side waits between sweeps.</summary>
    TimeSpan Delay { get; }

    /// <summary>Deletes this side's Completed messages past their retention period.</summary>
    /// <returns>How many rows were deleted.</returns>
    Task<int> ExecuteAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);
}
