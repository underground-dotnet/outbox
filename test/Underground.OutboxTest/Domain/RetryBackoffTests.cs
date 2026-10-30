using Underground.Outbox.Data;
using Underground.Outbox.Domain;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The delay law on its own, with jitter switched off so that the growth and the ceiling are exact.
/// The jitter test is the one place where randomness is the subject rather than a nuisance.
/// </summary>
public class RetryBackoffTests
{
    [Fact]
    public void DelayDoublesWithEveryFailedAttempt()
    {
        // Arrange
        var backoff = new RetryBackoff<OutboxMessage>(TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), jitter: 0);

        // Act
        var delays = Enumerable.Range(0, 5).Select(backoff.DelayFor);

        // Assert
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)],
            delays);
    }

    [Fact]
    public void DelayStopsGrowingAtTheConfiguredMaximum()
    {
        // Arrange
        var backoff = new RetryBackoff<OutboxMessage>(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), jitter: 0);

        // Act
        var delays = Enumerable.Range(0, 6).Select(backoff.DelayFor);

        // Assert
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)],
            delays);
    }

    [Fact]
    public void DelayStaysAtTheMaximumForAMessageThatHasFailedFarTooOftenToDouble()
    {
        // Arrange
        var backoff = new RetryBackoff<OutboxMessage>(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10), jitter: 0);

        // Act
        // a message that fails forever passes the point where doubling the base overflows a TimeSpan
        var delay = backoff.DelayFor(1_000);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void JitterVariesTheDelayWithinTheConfiguredProportion()
    {
        // Arrange
        var backoff = new RetryBackoff<OutboxMessage>(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), jitter: 0.2);

        // Act
        var delays = Enumerable.Range(0, 50).Select(_ => backoff.DelayFor(0)).ToList();

        // Assert
        Assert.All(delays, delay => Assert.InRange(delay, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)));
        // two Groups backing off at the same instant must not come back at the same instant
        Assert.True(delays.Distinct().Count() > 1, "jitter produced the same delay every time");
    }

    [Fact]
    public void NoJitterMeansTheDelayIsExact()
    {
        // Arrange
        var backoff = new RetryBackoff<OutboxMessage>(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), jitter: 0);

        // Act
        var delays = Enumerable.Range(0, 10).Select(_ => backoff.DelayFor(0));

        // Assert
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(10), delay));
    }
}
