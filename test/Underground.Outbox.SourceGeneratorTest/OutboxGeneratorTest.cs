using Microsoft.CodeAnalysis;

using VerifyXunit;

namespace Underground.Outbox.SourceGeneratorTest;

public class OutboxGeneratorTest
{
    private static Task VerifySubset(GeneratorDriver driver) =>
        Verify(driver).IgnoreGeneratedResult(_ =>
            // this file is the same for every run
            _.HintName.Equals("OutboxDependencyInjection.g.cs", StringComparison.OrdinalIgnoreCase)
        );

    [Fact]
    public Task Generates_dependency_injection_source_even_without_handlers()
    {
        var driver = GeneratorTestHelper.Run("""
            namespace Sample;

            public sealed class Placeholder;
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Generates_dispatcher_for_local_outbox_handler()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(string Text);

            public sealed class TestMessageHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Generates_dispatcher_for_local_inbox_and_outbox_handlers()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record InboxMessageType(int Id);

            public sealed record OutboxMessageType(int Id);

            public sealed class InboxHandler : IInboxMessageHandler<InboxMessageType>
            {
                public Task HandleAsync(InboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class OutboxHandler : Underground.Outbox.IOutboxMessageHandler<OutboxMessageType>
            {
                public Task HandleAsync(OutboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return VerifySubset(driver);
    }

    [Fact]
    public Task Generates_discard_mapping_for_outbox_handler()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Data;
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            public sealed class TestHandler : IOutboxMessageHandler<TestMessage>
            {
                [DiscardOn(typeof(DataException))]
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Generates_discard_mapping_for_inbox_handler()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Data;
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            public sealed class TestHandler : IInboxMessageHandler<TestMessage>
            {
                [DiscardOn(typeof(DataException))]
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Omits_discard_mapping_for_handler_without_attribute()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            public sealed class TestHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Ignores_abstract_handler_classes()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            public abstract class AbstractHandler : IOutboxMessageHandler<TestMessage>
            {
                public abstract Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken);
            }
            """);

        return VerifySubset(driver);
    }

    [Fact]
    public Task Ignores_non_handler_classes()
    {
        var driver = GeneratorTestHelper.Run("""
            namespace Sample;

            public interface IOutboxMessageHandler<T>;

            public sealed class TestMessage;

            public sealed class LooksLikeHandler : IOutboxMessageHandler<TestMessage>;
            """);

        return VerifySubset(driver);
    }
}
