using System.Collections.Concurrent;

namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// The probes the Handler and the exception policy saw, in the order they ran.
/// </summary>
public static class RecordedScopes
{
    public static ConcurrentQueue<ScopeProbe> Handler { get; } = new();
    public static ConcurrentQueue<ScopeProbe> Policy { get; } = new();

    public static void Clear()
    {
        Handler.Clear();
        Policy.Clear();
    }
}
