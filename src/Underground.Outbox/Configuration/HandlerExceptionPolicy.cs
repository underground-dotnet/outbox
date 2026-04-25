namespace Underground.Outbox.Configuration;

internal sealed record HandlerExceptionPolicy(Type ExceptionType, HandlerExceptionAction Action);
