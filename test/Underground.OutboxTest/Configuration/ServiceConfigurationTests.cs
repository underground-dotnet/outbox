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

        Assert.Equal("CleanupDelaySeconds", exception.ParamName);
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

        Assert.Equal("ProcessedMessageRetention", exception.ParamName);
        Assert.Contains("ProcessedMessageRetention", exception.Message, StringComparison.Ordinal);
    }
}
