namespace Underground.Outbox.Configuration;

public sealed class HandlerExceptionPolicyBuilder
{
    private readonly HandlerRegistrationBuilder _builder;
    private readonly Type _exceptionType;

    internal HandlerExceptionPolicyBuilder(HandlerRegistrationBuilder builder, Type exceptionType)
    {
        _builder = builder;
        _exceptionType = exceptionType;
    }

    public HandlerRegistrationBuilder Discard()
    {
        _builder.AddPolicy(_exceptionType, HandlerExceptionAction.Discard);
        return _builder;
    }
}
