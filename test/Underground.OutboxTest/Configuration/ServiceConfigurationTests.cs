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
    public void Validate_ThrowsArgumentOutOfRangeException_WhenProcessedMessageRetentionIsNegative()
    {
        var configuration = new InboxServiceConfiguration
        {
            ProcessedMessageRetention = TimeSpan.FromSeconds(-1)
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("ProcessedMessageRetention", exception.Message, StringComparison.Ordinal);
    }
}
