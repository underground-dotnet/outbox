using Underground.Outbox.Configuration.ExceptionPolicies;
using Underground.Outbox.Configuration.Policies;
using Underground.Outbox.Data;

namespace Underground.OutboxTest.TestPolicies;

public static class ExceptionPolicyBuilderExtensions
{
    extension<T>(ExceptionPolicyBuilder<T> builder) where T : class, IMessage
    {
        public PolicyBuilder<T> MarkAsProcessed()
        {
            builder.Target.AddExceptionPolicy(new MarkAsProcessedExceptionPolicy<T>(builder.ExceptionType));
            return builder.PolicyBuilder;
        }
    }
}