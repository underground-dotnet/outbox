namespace Underground.Outbox.SourceGeneratorTest;

public class OutboxGeneratorTest
{
    [Fact]
    public void Generates_dependency_injection_source_even_without_handlers()
    {
        var result = GeneratorTestHelper.Run("""
            namespace Sample;

            public sealed class Placeholder;
            """);

        var source = result.GetGeneratedSource("OutboxDependencyInjection.g.cs");

        Assert.Contains("public static class ConfigureOutboxServices", source);
        Assert.Contains("AddOutboxServices<TContext>", source);
        Assert.Contains("AddInboxServices<TContext>", source);
    }

    [Fact]
    public void Generates_dispatcher_for_local_outbox_handler()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage : IMessage
            {
                public long Id { get; }
                public Guid EventId { get; init; }
                public string Type => "Sample.TestMessage";
                public string PartitionKey => "partition";
                public string Data => "{}";
                public int RetryCount { get; set; }
                public DateTime? ProcessedAt { get; set; }
            }

            public sealed class TestMessageHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        var source = result.GetGeneratedSource("GeneratedDispatcher.g.cs");

        Assert.Contains("case \"Sample.TestMessage\":", source);
        Assert.Contains("JsonSerializer.Deserialize<Sample.TestMessage>(message.Data)", source);
        Assert.Contains("GetRequiredService<IOutboxMessageHandler<Sample.TestMessage>>()", source);
        Assert.DoesNotContain("DateTime.UtcNow", source);
    }

    [Fact]
    public void Generates_dispatcher_for_local_inbox_and_outbox_handlers()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record InboxMessageType : IMessage
            {
                public long Id { get; }
                public Guid EventId { get; init; }
                public string Type => "Sample.InboxMessageType";
                public string PartitionKey => "partition";
                public string Data => "{}";
                public int RetryCount { get; set; }
                public DateTime? ProcessedAt { get; set; }
            }

            public sealed record OutboxMessageType : IMessage
            {
                public long Id { get; }
                public Guid EventId { get; init; }
                public string Type => "Sample.OutboxMessageType";
                public string PartitionKey => "partition";
                public string Data => "{}";
                public int RetryCount { get; set; }
                public DateTime? ProcessedAt { get; set; }
            }

            public sealed class InboxHandler : IInboxMessageHandler<InboxMessageType>
            {
                public Task HandleAsync(InboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class OutboxHandler : IOutboxMessageHandler<OutboxMessageType>
            {
                public Task HandleAsync(OutboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        var source = result.GetGeneratedSource("GeneratedDispatcher.g.cs");

        Assert.Contains("case \"Sample.InboxMessageType\":", source);
        Assert.Contains("GetRequiredService<IInboxMessageHandler<Sample.InboxMessageType>>()", source);
        Assert.Contains("case \"Sample.OutboxMessageType\":", source);
        Assert.Contains("GetRequiredService<IOutboxMessageHandler<Sample.OutboxMessageType>>()", source);
    }

    [Fact]
    public void Ignores_abstract_handler_classes()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage : IMessage
            {
                public long Id { get; }
                public Guid EventId { get; init; }
                public string Type => "Sample.TestMessage";
                public string PartitionKey => "partition";
                public string Data => "{}";
                public int RetryCount { get; set; }
                public DateTime? ProcessedAt { get; set; }
            }

            public abstract class AbstractHandler : IOutboxMessageHandler<TestMessage>
            {
                public abstract Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken);
            }
            """);

        var source = result.GetGeneratedSource("GeneratedDispatcher.g.cs");

        Assert.DoesNotContain("case \"Sample.TestMessage\":", source);
        Assert.DoesNotContain("AbstractHandler", source);
    }

    [Fact]
    public void Ignores_non_handler_classes()
    {
        var result = GeneratorTestHelper.Run("""
            namespace Sample;

            public interface IOutboxMessageHandler<T>;

            public sealed class TestMessage;

            public sealed class LooksLikeHandler : IOutboxMessageHandler<TestMessage>;
            """);

        var source = result.GetGeneratedSource("GeneratedDispatcher.g.cs");

        Assert.DoesNotContain("case \"Sample.TestMessage\":", source);
        Assert.DoesNotContain("LooksLikeHandler", source);
    }

    [Fact]
    public void Supports_fully_qualified_handler_interfaces()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            namespace Sample;

            public sealed record QualifiedMessage : Underground.Outbox.Data.IMessage
            {
                public long Id { get; }
                public Guid EventId { get; init; }
                public string Type => "Sample.QualifiedMessage";
                public string PartitionKey => "partition";
                public string Data => "{}";
                public int RetryCount { get; set; }
                public DateTime? ProcessedAt { get; set; }
            }

            public sealed class QualifiedInboxHandler : Underground.Outbox.IInboxMessageHandler<QualifiedMessage>
            {
                public Task HandleAsync(QualifiedMessage message, Underground.Outbox.Data.MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class QualifiedOutboxHandler : Underground.Outbox.IOutboxMessageHandler<QualifiedMessage>
            {
                public Task HandleAsync(QualifiedMessage message, Underground.Outbox.Data.MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        var source = result.GetGeneratedSource("GeneratedDispatcher.g.cs");

        Assert.Contains("case \"Sample.QualifiedMessage\":", source);
        Assert.Contains("GetRequiredService<IInboxMessageHandler<Sample.QualifiedMessage>>()", source);
        Assert.Contains("GetRequiredService<IOutboxMessageHandler<Sample.QualifiedMessage>>()", source);
    }
}
