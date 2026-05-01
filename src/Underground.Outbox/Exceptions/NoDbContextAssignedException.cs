namespace Underground.Outbox.Exceptions;

internal class NoDbContextAssignedException : InvalidOperationException
{
    internal NoDbContextAssignedException() : base("DbContextType is not set. Use UseDbContext<TDbContext>() to set it.")
    {
    }
}