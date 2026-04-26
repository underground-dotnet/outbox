using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;

namespace Underground.Outbox.Configuration.HandlerRegistrations;

public class HandlerRegistrationBuilder<TEntity> where TEntity : class, IMessage
{
    internal readonly HandlerRegistration<TEntity> Registration;

    internal HandlerRegistrationBuilder(HandlerRegistration<TEntity> registration)
    {
        Registration = registration;
    }

    public ExceptionPolicyBuilder<TEntity> OnException<TException>() where TException : Exception
    {
        return new ExceptionPolicyBuilder<TEntity>(this, typeof(TException));
    }
}
