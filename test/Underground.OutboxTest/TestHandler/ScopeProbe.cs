namespace Underground.OutboxTest.TestHandler;

/// <summary>
/// Registered Scoped, so two resolutions return the same instance exactly when they came from the same scope.
/// </summary>
public sealed class ScopeProbe
{
    public Guid Id { get; } = Guid.NewGuid();
}
