namespace Underground.Outbox.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class DiscardOnAttribute : Attribute
{
    public Type[] ExceptionTypes { get; }

    public DiscardOnAttribute(params Type[] exceptionTypes)
    {
        if (exceptionTypes == null || exceptionTypes.Length == 0)
        {
            throw new ArgumentException("At least one exception type must be provided.", nameof(exceptionTypes));
        }

        foreach (var exceptionType in exceptionTypes)
        {
            if (!typeof(Exception).IsAssignableFrom(exceptionType))
            {
                throw new ArgumentException("All types must be Exceptions.", nameof(exceptionTypes));
            }
        }

        ExceptionTypes = exceptionTypes;
    }
}
