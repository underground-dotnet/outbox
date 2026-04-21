namespace Underground.Outbox.Domain.ExceptionHandlers;

public interface IDiscardOnExceptionMapping
{
    IReadOnlySet<Type>? GetDiscardOnTypes(Type handlerType);
}
