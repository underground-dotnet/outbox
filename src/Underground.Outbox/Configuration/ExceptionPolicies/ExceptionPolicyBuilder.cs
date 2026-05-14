using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

public sealed class ExceptionPolicyBuilder<TEntity> where TEntity : class, IMessage
{
    private readonly IPolicyStore<TEntity> _target;
    private readonly PolicyBuilder<TEntity> _policyBuilder;
    private readonly Type _exceptionType;

    internal ExceptionPolicyBuilder(IPolicyStore<TEntity> target, PolicyBuilder<TEntity> policyBuilder, Type exceptionType)
    {
        _target = target;
        _policyBuilder = policyBuilder;
        _exceptionType = exceptionType;
    }

    // public HandlerRegistrationBuilder<TEntity> Custom(ExceptionPolicy<TEntity> policy)
    // {
    //     _builder.Registration.ExceptionPolicies.Add(policy);
    //     return _builder;
    // }

    public PolicyBuilder<TEntity> Discard()
    {
        _target.AddExceptionPolicy(new DiscardExceptionPolicy<TEntity>(_exceptionType));
        return _policyBuilder;
    }
}
