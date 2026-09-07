using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Underground.OutboxTest;

/// <summary>
/// Fails the first few executions of any statement containing a marker, then lets them through. Which
/// statement fails is what a test uses to place a transient failure inside a chosen transaction.
/// </summary>
public sealed class TransientFaultInterceptor(string marker, int faults) : DbCommandInterceptor
{
    private int _remaining = faults;

    /// <summary>How many statements this actually failed, so a test can assert the fault was reached.</summary>
    public int Injected { get; private set; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        FailIfMatched(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        FailIfMatched(command);
        return ValueTask.FromResult(result);
    }

    private void FailIfMatched(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_remaining <= 0 || !command.CommandText.Contains(marker, StringComparison.Ordinal))
        {
            return;
        }

        _remaining--;
        Injected++;

        throw new TransientTestException($"Injected transient failure on a statement containing '{marker}'");
    }
}
