
using Microsoft.EntityFrameworkCore.Storage;

namespace Underground.OutboxTest;

/// <summary>
/// Retries <see cref="TransientTestException"/> and nothing else, so that a test can drive a replay without
/// making every other failure retry as well.
/// </summary>
/// <remarks>
/// It reports <c>RetriesOnFailure</c>, which is what makes EF refuse a caller-begun transaction - the same
/// refusal a host gets from Aspire's <c>EnrichNpgsqlDbContext</c>.
/// </remarks>
public sealed class TransientTestExecutionStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
{
    protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;
}
