using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.Policies;

internal interface IPolicyStore<TEntity> where TEntity : class, IMessage
{
    void AddExceptionPolicy(ExceptionPolicy<TEntity> policy);
}
