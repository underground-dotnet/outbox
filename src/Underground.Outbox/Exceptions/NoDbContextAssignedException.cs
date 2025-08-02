namespace Underground.Outbox.Exceptions;

public class NoDbContextAssignedException : InvalidOperationException
{
    public NoDbContextAssignedException() : base("DbContextType is not set. Use UseDbContext<TDbContext>() to set it.")
    {
    }
}