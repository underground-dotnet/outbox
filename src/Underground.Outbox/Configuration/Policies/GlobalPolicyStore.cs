using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.Policies;

public sealed class GlobalPolicyStore<TEntity> : IPolicyStore<TEntity> where TEntity : class, IMessage
{
    internal List<ExceptionPolicy<TEntity>> ExceptionPolicies { get; } = [];

    public void AddExceptionPolicy(ExceptionPolicy<TEntity> policy)
    {
        ExceptionPolicies.Add(policy);
    }
}
