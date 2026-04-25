using System.Data;

using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Configuration;

public class ServiceConfigurationTests
{
    [Fact]
    public void AddHandler_ReturnsBuilderThatStoresDiscardRule()
    {
        var config = new OutboxServiceConfiguration();

        config.AddHandler<DiscardFailedMessageHandler>()
            .OnException<DataException>().Discard();

        var registration = Assert.Single(config.HandlerRegistrations);
        Assert.Equal(typeof(IOutboxMessageHandler<DiscardMessage>), registration.ServiceType);
        Assert.Equal(typeof(DiscardFailedMessageHandler), registration.ImplementationType);

        var policy = Assert.Single(registration.ExceptionPolicies);
        Assert.Equal(typeof(DataException), policy.ExceptionType);
        Assert.Equal(HandlerExceptionAction.Discard, policy.Action);
    }

    [Fact]
    public void Validate_Throws_WhenHandlerDoesNotImplementCurrentScopeInterface()
    {
        var config = new InboxServiceConfiguration();

        Assert.Throws<ArgumentException>(() => config.AddHandler<DiscardFailedMessageHandler>());
    }

    [Fact]
    public void Validate_Throws_WhenConfiguredExceptionPolicyTypeIsNotAnException()
    {
        var config = new OutboxServiceConfiguration();
        var registration = new HandlerRegistration(
            typeof(IOutboxMessageHandler<DiscardMessage>),
            typeof(DiscardFailedMessageHandler),
            ServiceLifetime.Transient);

        registration.ExceptionPolicies.Add(new HandlerExceptionPolicy(typeof(string), HandlerExceptionAction.Discard));
        config.HandlerRegistrations.Add(registration);

        Assert.Throws<ArgumentException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenBatchSizeIsNotPositive()
    {
        var config = new OutboxServiceConfiguration
        {
            BatchSize = 0,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenParallelProcessingOfPartitionsIsNotPositive()
    {
        var config = new OutboxServiceConfiguration
        {
            ParallelProcessingOfPartitions = 0,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Throws_WhenProcessingDelayMillisecondsIsNegative()
    {
        var config = new OutboxServiceConfiguration
        {
            ProcessingDelayMilliseconds = -1,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
    }
}
