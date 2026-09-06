using Underground.Outbox.Configuration;

namespace Underground.OutboxTest.Configuration;

public class ServiceConfigurationTests
{
    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCleanupDelaySecondsIsZero()
    {
        var configuration = new OutboxServiceConfiguration
        {
            CleanupDelaySeconds = 0
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("CleanupDelaySeconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenHandlerTimeoutIsZero()
    {
        var configuration = new OutboxServiceConfiguration
        {
            HandlerTimeout = TimeSpan.Zero
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("HandlerTimeout", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBackoffBaseIsZero()
    {
        var configuration = new OutboxServiceConfiguration
        {
            BackoffBase = TimeSpan.Zero
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("BackoffBase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenMaxBackoffIsShorterThanBackoffBase()
    {
        var configuration = new OutboxServiceConfiguration
        {
            BackoffBase = TimeSpan.FromMinutes(1),
            MaxBackoff = TimeSpan.FromSeconds(1)
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("MaxBackoff", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBackoffJitterIsOutsideItsRange(double jitter)
    {
        var configuration = new InboxServiceConfiguration
        {
            BackoffJitter = jitter
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("BackoffJitter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCompletedMessageRetentionIsNegative()
    {
        var configuration = new InboxServiceConfiguration
        {
            CompletedMessageRetention = TimeSpan.FromSeconds(-1)
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("CompletedMessageRetention", exception.Message, StringComparison.Ordinal);
    }
}
