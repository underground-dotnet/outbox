using Underground.Outbox.Configuration.HandlerRegistrations;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.ExceptionPolicies;

public sealed class ExceptionPolicyBuilder<TEntity> where TEntity : class, IMessage
{
    private readonly HandlerRegistrationBuilder<TEntity> _builder;
    private readonly Type _exceptionType;

    internal ExceptionPolicyBuilder(HandlerRegistrationBuilder<TEntity> builder, Type exceptionType)
    {
        _builder = builder;
        _exceptionType = exceptionType;
    }

    // public HandlerRegistrationBuilder<TEntity> Custom(ExceptionPolicy<TEntity> policy)
    // {
    //     _builder.Registration.ExceptionPolicies.Add(policy);
    //     return _builder;
    // }

    public HandlerRegistrationBuilder<TEntity> Discard()
    {
        _builder.Registration.ExceptionPolicies.Add(new DiscardExceptionPolicy<TEntity>(_exceptionType));
        return _builder;
    }
}
