using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.Dispatchers;
using Underground.Outbox.Domain.ExceptionHandlers;

namespace Underground.Outbox.Domain.Inboxes;

/// <summary>
/// One Inbox as the host runs it: its own Handlers, settings, workers and context, so that nothing one
/// Inbox does - a backlog, a poison message, a slow Handler's backoff - is decided by another's.
/// </summary>
internal sealed class Inbox
{
    internal Inbox(
        InboxRegistration registration,
        IEnumerable<HandlerEntry<InboxMessage>> entries,
        IServiceScopeFactory rootScopeFactory,
        ILoggerFactory loggerFactory)
    {
        Name = registration.Name;
        ContextType = registration.ContextType;
        Configuration = registration.Configuration;

        Registry = new HandlerRegistry<InboxMessage>(entries);
        Backoff = new RetryBackoff<InboxMessage>(Configuration.BackoffBase, Configuration.MaxBackoff, Configuration.BackoffJitter);
        Policies = new ExceptionPolicyTable<InboxMessage>(Configuration.Registrations, Configuration.GlobalPolicies.ExceptionPolicies);
        ScopeFactory = new InboxScopeFactory(rootScopeFactory, this);
        Processor = new ConcurrentProcessor<InboxMessage>(
            loggerFactory.CreateLogger<ConcurrentProcessor<InboxMessage>>(), ScopeFactory, Configuration);
    }

    internal string Name { get; }

    /// <summary>The context this Inbox's transaction runs on, and so the one its Handlers write through.</summary>
    internal Type ContextType { get; }

    internal ServiceConfiguration<InboxMessage> Configuration { get; }

    internal HandlerRegistry<InboxMessage> Registry { get; }

    internal RetryBackoff<InboxMessage> Backoff { get; }

    internal ExceptionPolicyTable<InboxMessage> Policies { get; }

    /// <summary>Opens scopes that work for this Inbox.</summary>
    internal IServiceScopeFactory ScopeFactory { get; }

    internal ConcurrentProcessor<InboxMessage> Processor { get; }
}
