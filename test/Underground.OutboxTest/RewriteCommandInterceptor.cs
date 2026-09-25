using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Underground.OutboxTest;

/// <summary>
/// Hands the first statement containing a marker to a rewrite just before it is sent. What the rewrite does
/// is how a test places an interleaving or a fault at one chosen statement.
/// </summary>
public sealed class RewriteCommandInterceptor(string marker, Func<DbCommand, CancellationToken, Task> rewrite) : DbCommandInterceptor
{
    /// <summary>Whether the marked statement was reached, so a test can assert its setup took effect.</summary>
    public bool Rewritten { get; private set; }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        await RewriteIfMatchedAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        await RewriteIfMatchedAsync(command, cancellationToken);
        return result;
    }

    private async Task RewriteIfMatchedAsync(DbCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (Rewritten || !command.CommandText.Contains(marker, StringComparison.Ordinal))
        {
            return;
        }

        Rewritten = true;

        await rewrite(command, cancellationToken);
    }
}
