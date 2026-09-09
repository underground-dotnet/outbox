using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Configuration;

public class ServiceConfigurationTests
{
    private static OutboxServiceConfiguration<TestDbContext> Outbox() => new() { Schema = TestSchemas.Default };

    private static InboxServiceConfiguration<InboxOutboxDbContext> Inbox() => new() { Schema = TestSchemas.Default };

    [Fact]
    public void Validate_ThrowsArgumentException_WhenSchemaIsUnset()
    {
        var configuration = new OutboxServiceConfiguration<TestDbContext>();

        var exception = Assert.Throws<ArgumentException>(() => configuration.Validate());

        Assert.Contains("Schema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCleanupDelaySecondsIsZero()
    {
        var configuration = Outbox();
        configuration.CleanupDelaySeconds = 0;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("CleanupDelaySeconds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenHandlerTimeoutIsZero()
    {
        var configuration = Outbox();
        configuration.HandlerTimeout = TimeSpan.Zero;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("HandlerTimeout", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBackoffBaseIsZero()
    {
        var configuration = Outbox();
        configuration.BackoffBase = TimeSpan.Zero;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("BackoffBase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenMaxBackoffIsShorterThanBackoffBase()
    {
        var configuration = Outbox();
        configuration.BackoffBase = TimeSpan.FromMinutes(1);
        configuration.MaxBackoff = TimeSpan.FromSeconds(1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("MaxBackoff", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.0)]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenBackoffJitterIsOutsideItsRange(double jitter)
    {
        var configuration = Inbox();
        configuration.BackoffJitter = jitter;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("BackoffJitter", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ThrowsArgumentOutOfRangeException_WhenCompletedMessageRetentionIsNegative()
    {
        var configuration = Inbox();
        configuration.CompletedMessageRetention = TimeSpan.FromSeconds(-1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => configuration.Validate());

        Assert.Contains("CompletedMessageRetention", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOutboxServices_Throws_WhenNoSchemaWasNamed()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(
            () => services.AddModuleADbContextOutboxServices(cfg => cfg.AddHandler<ModuleAHandler, SharedContract>()));

        Assert.Contains("Schema", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOutboxServices_Throws_WhenAnotherContextAlreadyClaimedTheSchema()
    {
        var services = new ServiceCollection();
        services.AddModuleADbContextOutboxServices(cfg =>
        {
            cfg.Schema = "shared";
            cfg.AddHandler<ModuleAHandler, SharedContract>();
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddModuleBDbContextOutboxServices(cfg =>
            {
                cfg.Schema = "shared";
                cfg.AddHandler<ModuleBHandler, SharedContract>();
            }));

        Assert.Contains(nameof(ModuleADbContext), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ModuleBDbContext), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOutboxServices_Succeeds_WhenTwoContextsNameDifferentSchemas()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() =>
        {
            services.AddModuleADbContextOutboxServices(cfg =>
            {
                cfg.Schema = ModuleADbContext.Schema;
                cfg.AddHandler<ModuleAHandler, SharedContract>();
            });
            services.AddModuleBDbContextOutboxServices(cfg =>
            {
                cfg.Schema = ModuleBDbContext.Schema;
                cfg.AddHandler<ModuleBHandler, SharedContract>();
            });
        });

        Assert.Null(exception);
    }
}
