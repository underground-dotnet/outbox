using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Domain.Inboxes;

/// <summary>
/// Opens scopes bound to one Inbox, so its workers and its cleanup see that Inbox's settings and context.
/// </summary>
internal sealed class InboxScopeFactory(IServiceScopeFactory scopeFactory, Inbox inbox) : IServiceScopeFactory
{
    public IServiceScope CreateScope()
    {
        var scope = scopeFactory.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<InboxScope>().Inbox = inbox;
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
