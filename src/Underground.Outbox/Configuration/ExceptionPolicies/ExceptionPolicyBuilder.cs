using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

public sealed class ExceptionPolicyBuilder<TEntity> where TEntity : class, IMessage
{
    internal readonly IPolicyStore<TEntity> Target;
    internal readonly PolicyBuilder<TEntity> PolicyBuilder;
    internal readonly Type ExceptionType;

    internal ExceptionPolicyBuilder(IPolicyStore<TEntity> target, PolicyBuilder<TEntity> policyBuilder, Type exceptionType)
    {
        Target = target;
        PolicyBuilder = policyBuilder;
        ExceptionType = exceptionType;
    }

    public PolicyBuilder<TEntity> Discard()
    {
        Target.AddExceptionPolicy(new DiscardExceptionPolicy<TEntity>(ExceptionType));
        return PolicyBuilder;
    }
}
