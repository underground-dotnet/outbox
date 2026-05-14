using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.Policies;

public class PolicyBuilder<TEntity> where TEntity : class, IMessage
{
    private readonly IPolicyStore<TEntity> _policyStore;

    internal PolicyBuilder(IPolicyStore<TEntity> policyStore)
    {
        _policyStore = policyStore;
    }

    public ExceptionPolicyBuilder<TEntity> OnException<TException>() where TException : Exception
    {
        return new ExceptionPolicyBuilder<TEntity>(_policyStore, this, typeof(TException));
    }
}
