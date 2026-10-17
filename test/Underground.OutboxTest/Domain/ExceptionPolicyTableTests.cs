using System.Data;

using Underground.Outbox.Configuration;
using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Data;
using Underground.Outbox.Domain.ExceptionHandlers;
using Underground.OutboxTest.TestHandler;
using Underground.OutboxTest.TestPolicies;

namespace Underground.OutboxTest.Domain;

/// <summary>
/// The rules for choosing an exception policy, configured through the same fluent calls an application
/// makes and asked of the table directly - no Handler, no database.
/// </summary>
public class ExceptionPolicyTableTests
{
    private readonly OutboxServiceConfiguration _config = new();

    [Fact]
    public void HandlerPolicyWinsOverGlobalPolicy()
    {
        _config.Policies.OnException<DataException>().Discard();
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>().OnException<DataException>().MarkAsCompleted();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new DataException());

        Assert.IsType<MarkAsCompletedExceptionPolicy<OutboxMessage>>(policy);
    }

    [Fact]
    public void HandlerPolicyWinsOverMoreSpecificGlobalPolicy()
    {
        _config.Policies.OnException<DataException>().Discard();
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>().OnException<Exception>().MarkAsCompleted();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new DataException());

        Assert.IsType<MarkAsCompletedExceptionPolicy<OutboxMessage>>(policy);
    }

    [Fact]
    public void NearestExceptionTypeWinsWhateverTheRegistrationOrder()
    {
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>()
            .OnException<Exception>().Discard()
            .OnException<DataException>().MarkAsCompleted();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new DataException());

        Assert.IsType<MarkAsCompletedExceptionPolicy<OutboxMessage>>(policy);
    }

    [Fact]
    public void BaseTypePolicyMatchesDerivedException()
    {
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>().OnException<SystemException>().Discard();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new DataException());

        Assert.Equal(typeof(SystemException), policy?.ExceptionType);
    }

    [Fact]
    public void DuplicateExceptionTypeKeepsTheFirstRegistration()
    {
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>()
            .OnException<DataException>().Discard()
            .OnException<DataException>().MarkAsCompleted();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new DataException());

        Assert.IsType<DiscardExceptionPolicy<OutboxMessage>>(policy);
    }

    [Fact]
    public void HandlerPolicyAppliesOnlyToItsConfiguredMessageType()
    {
        _config.ForHandler<FailedMultipleMessagesHandler, FailedMultiMessageA>().OnException<InvalidOperationException>().Discard();

        var forA = Select<FailedMultipleMessagesHandler, FailedMultiMessageA>(new InvalidOperationException());
        var forB = Select<FailedMultipleMessagesHandler, FailedMultiMessageB>(new InvalidOperationException());

        Assert.NotNull(forA);
        Assert.Null(forB);
    }

    [Fact]
    public void HandlerWithoutAMatchingPolicyFallsBackToGlobal()
    {
        _config.Policies.OnException<DataException>().Discard();
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>().OnException<InvalidOperationException>().MarkAsCompleted();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new DataException());

        Assert.IsType<DiscardExceptionPolicy<OutboxMessage>>(policy);
    }

    [Fact]
    public void NoMatchingPolicyReturnsNull()
    {
        _config.Policies.OnException<DataException>().Discard();
        _config.ForHandler<DiscardFailedMessageHandler, DiscardMessage>().OnException<DataException>().Discard();

        var policy = Select<DiscardFailedMessageHandler, DiscardMessage>(new InvalidOperationException());

        Assert.Null(policy);
    }

    private ExceptionPolicy<OutboxMessage>? Select<THandler, TMessage>(Exception thrown)
    {
        var table = new ExceptionPolicyTable<OutboxMessage>(_config.Registrations, _config.GlobalPolicies.ExceptionPolicies);

        return table.Select(typeof(THandler), typeof(TMessage), thrown);
    }
}
