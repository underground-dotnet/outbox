using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;

namespace Underground.Outbox.Domain;

/// <summary>
/// One registered side's entry in the shared cleanup loop: the delay it wants, and the scoped deleter it
/// runs.
/// </summary>
internal sealed class RetentionSweep<TContext, TEntity>(ServiceConfiguration<TContext, TEntity> config) : IRetentionSweep
    where TContext : DbContext
    where TEntity : class, IMessage
{
    public string Name { get; } = SideName.For<TContext, TEntity>();

    public TimeSpan Delay { get; } = TimeSpan.FromSeconds(config.CleanupDelaySeconds);

    public Task<int> ExecuteAsync(IServiceProvider scopedServices, CancellationToken cancellationToken)
        => scopedServices.GetRequiredService<DeleteCompletedMessages<TContext, TEntity>>().ExecuteAsync(cancellationToken);
}
