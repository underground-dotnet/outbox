using Underground.Outbox.Configuration;

namespace Underground.OutboxTest.Configuration;

public class ServiceConfigurationTests
{
    public static TheoryData<TimeSpan> HandlerTimeoutsOutOfRange =>
        [TimeSpan.FromMilliseconds(9), TimeSpan.FromHours(24) + TimeSpan.FromTicks(1)];

    public static TheoryData<TimeSpan> HandlerTimeoutsInRange =>
        [TimeSpan.FromMilliseconds(10), TimeSpan.FromHours(24)];

    [Theory]
    [MemberData(nameof(HandlerTimeoutsOutOfRange))]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenHandlerTimeoutIsOutsideItsRange(TimeSpan timeout)
    {
        // Arrange
        var configuration = new OutboxServiceConfiguration
        {
            HandlerTimeout = timeout
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());
        Assert.Contains("HandlerTimeout", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HandlerTimeoutsInRange))]
    public void Validate_Accepts_HandlerTimeoutAtItsBounds(TimeSpan timeout)
    {
        // Arrange
        var configuration = new OutboxServiceConfiguration
        {
            HandlerTimeout = timeout
        };

        // Act
        var exception = Record.Exception(configuration.Validate);

        // Assert
        Assert.Null(exception);
    }

    public static TheoryData<TimeSpan> CleanupIntervalsOutOfRange =>
        [TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1), TimeSpan.FromDays(31) + TimeSpan.FromTicks(1)];

    public static TheoryData<TimeSpan> CleanupIntervalsInRange =>
        [TimeSpan.FromSeconds(1), TimeSpan.FromDays(31)];

    [Theory]
    [MemberData(nameof(CleanupIntervalsOutOfRange))]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCleanupIntervalIsOutsideItsRange(TimeSpan interval)
    {
        // Arrange
        var configuration = new OutboxServiceConfiguration
        {
            CleanupInterval = interval
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());
        Assert.Contains("CleanupInterval", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CleanupIntervalsInRange))]
    public void Validate_Accepts_CleanupIntervalAtItsBounds(TimeSpan interval)
    {
        // Arrange
        var configuration = new OutboxServiceConfiguration
        {
            CleanupInterval = interval
        };

        // Act
        var exception = Record.Exception(configuration.Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBackoffBaseIsZero()
    {
        // Arrange
        var configuration = new OutboxServiceConfiguration
        {
            BackoffBase = TimeSpan.Zero
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());
        Assert.Contains("BackoffBase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenMaxBackoffIsShorterThanBackoffBase()
    {
        // Arrange
        var configuration = new OutboxServiceConfiguration
        {
            BackoffBase = TimeSpan.FromMinutes(1),
            MaxBackoff = TimeSpan.FromSeconds(1)
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());
        Assert.Contains("MaxBackoff", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBackoffJitterIsOutsideItsRange(double jitter)
    {
        // Arrange
        var configuration = new InboxServiceConfiguration
        {
            BackoffJitter = jitter
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());
        Assert.Contains("BackoffJitter", exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<TimeSpan> RetentionsOutOfRange =>
        [TimeSpan.FromTicks(-1), TimeSpan.FromDays(365) + TimeSpan.FromTicks(1)];

    public static TheoryData<TimeSpan> RetentionsInRange =>
        [TimeSpan.Zero, TimeSpan.FromDays(365)];

    [Theory]
    [MemberData(nameof(RetentionsOutOfRange))]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCompletedMessageRetentionIsOutsideItsRange(TimeSpan retention)
    {
        // Arrange
        var configuration = new InboxServiceConfiguration
        {
            CompletedMessageRetention = retention
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());
        Assert.Contains("CompletedMessageRetention", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RetentionsInRange))]
    public void Validate_Accepts_CompletedMessageRetentionAtItsBounds(TimeSpan retention)
    {
        // Arrange
        var configuration = new InboxServiceConfiguration
        {
            CompletedMessageRetention = retention
        };

        // Act
        var exception = Record.Exception(configuration.Validate);

        // Assert
        Assert.Null(exception);
    }
}
