

namespace Underground.OutboxTest;

/// <summary>
/// The failure <see cref="TransientTestExecutionStrategy"/> retries. A sentinel rather than a real Npgsql
/// error, so a test says which statement fails and how often instead of provoking the database into it.
/// </summary>
public sealed class TransientTestException(string message) : Exception(message);
