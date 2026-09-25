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
        var configuration = new OutboxServiceConfiguration
        {
            HandlerTimeout = timeout
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("HandlerTimeout", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HandlerTimeoutsInRange))]
    public void Validate_Accepts_HandlerTimeoutAtItsBounds(TimeSpan timeout)
    {
        var configuration = new OutboxServiceConfiguration
        {
            HandlerTimeout = timeout
        };

        configuration.Validate();
    }

    public static TheoryData<TimeSpan> CleanupIntervalsOutOfRange =>
        [TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1), TimeSpan.FromDays(31) + TimeSpan.FromTicks(1)];

    public static TheoryData<TimeSpan> CleanupIntervalsInRange =>
        [TimeSpan.FromSeconds(1), TimeSpan.FromDays(31)];

    [Theory]
    [MemberData(nameof(CleanupIntervalsOutOfRange))]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCleanupIntervalIsOutsideItsRange(TimeSpan interval)
    {
        var configuration = new OutboxServiceConfiguration
        {
            CleanupInterval = interval
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("CleanupInterval", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CleanupIntervalsInRange))]
    public void Validate_Accepts_CleanupIntervalAtItsBounds(TimeSpan interval)
    {
        var configuration = new OutboxServiceConfiguration
        {
            CleanupInterval = interval
        };

        configuration.Validate();
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

    public static TheoryData<TimeSpan> RetentionsOutOfRange =>
        [TimeSpan.FromTicks(-1), TimeSpan.FromDays(365) + TimeSpan.FromTicks(1)];

    public static TheoryData<TimeSpan> RetentionsInRange =>
        [TimeSpan.Zero, TimeSpan.FromDays(365)];

    [Theory]
    [MemberData(nameof(RetentionsOutOfRange))]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCompletedMessageRetentionIsOutsideItsRange(TimeSpan retention)
    {
        var configuration = new InboxServiceConfiguration
        {
            CompletedMessageRetention = retention
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("CompletedMessageRetention", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RetentionsInRange))]
    public void Validate_Accepts_CompletedMessageRetentionAtItsBounds(TimeSpan retention)
    {
        var configuration = new InboxServiceConfiguration
        {
            CompletedMessageRetention = retention
        };

        configuration.Validate();
    }
}
