namespace Underground.Outbox.Configuration;

public sealed class HandlerRegistrationBuilder
{
    private readonly HandlerRegistration _registration;

    internal HandlerRegistrationBuilder(HandlerRegistration registration)
    {
        _registration = registration;
    }

    public HandlerExceptionPolicyBuilder OnException<TException>() where TException : Exception
    {
        return new HandlerExceptionPolicyBuilder(this, typeof(TException));
    }

    internal void AddPolicy(Type exceptionType, HandlerExceptionAction action)
    {
        _registration.ExceptionPolicies.Add(new HandlerExceptionPolicy(exceptionType, action));
    }
}
