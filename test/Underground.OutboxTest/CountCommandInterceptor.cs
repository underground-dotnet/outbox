using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Underground.OutboxTest;

/// <summary>
/// Counts the statements containing a marker, so a test can assert how many round trips some work took.
/// </summary>
public sealed class CountCommandInterceptor(string marker) : DbCommandInterceptor
{
    private int _count;

    public int Count => _count;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        CountIfMatched(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        CountIfMatched(command);
        return ValueTask.FromResult(result);
    }

    private void CountIfMatched(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.CommandText.Contains(marker, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _count);
        }
    }
}
