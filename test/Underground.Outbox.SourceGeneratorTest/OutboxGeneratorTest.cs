using Meziantou.Framework.SnapshotTesting;

using Microsoft.CodeAnalysis;

namespace Underground.Outbox.SourceGeneratorTest;

public class OutboxGeneratorTest
{
    [Fact]
    public void Generates_nothing_for_an_assembly_without_handlers()
    {
        var result = GeneratorTestHelper.Run("""
            namespace Sample;

            public sealed class Placeholder;
            """);

        AssertNothingGenerated(result);
    }

    [Fact]
    public void Generates_registration_for_local_outbox_handler()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(string Text);

            public sealed class TestMessageHandler : IOutboxMessageHandler<TestMessage>
            {
                public void HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Generates_registration_for_local_inbox_and_outbox_handlers()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record InboxMessageType(int Id);

            public sealed record OutboxMessageType(int Id);

            public sealed class InboxHandler : IInboxMessageHandler<InboxMessageType>
            {
                public void HandleAsync(InboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class OutboxHandler : Underground.Outbox.IOutboxMessageHandler<OutboxMessageType>
            {
                public void HandleAsync(OutboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Generates_registration_for_nested_message_type()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public static class Outer
            {
                public sealed record Inner(string Text);
            }

            public sealed class InnerHandler : IOutboxMessageHandler<Outer.Inner>
            {
                public void HandleAsync(Outer.Inner message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Generates_registration_for_generic_message_type()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record Payload(string Text);

            public sealed record Envelope<T>(T Body);

            public sealed class EnvelopeHandler : IOutboxMessageHandler<Envelope<Payload>>
            {
                public void HandleAsync(Envelope<Payload> message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Reports_competing_handlers_for_the_same_message_type()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(string Text);

            public sealed class FirstHandler : IOutboxMessageHandler<TestMessage>
            {
                public void HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class SecondHandler : IOutboxMessageHandler<TestMessage>
            {
                public void HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Generates_registration_for_handler_with_explicit_lifetime()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Microsoft.Extensions.DependencyInjection;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(string Text);

            [MessageHandlerLifetime(ServiceLifetime.Scoped)]
            public sealed class TestMessageHandler : IOutboxMessageHandler<TestMessage>
            {
                public void HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Generates_one_concrete_registration_for_handler_of_several_message_types()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Microsoft.Extensions.DependencyInjection;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record First(int Id);

            public sealed record Second(int Id);

            [MessageHandlerLifetime(ServiceLifetime.Scoped)]
            public sealed class BothHandler : IOutboxMessageHandler<First>, IOutboxMessageHandler<Second>
            {
                public void HandleAsync(First message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;

                public void HandleAsync(Second message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Snapshot.Validate(result);
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

            public sealed record TestMessage(int Id);

            public abstract class AbstractHandler : IOutboxMessageHandler<TestMessage>
            {
                public abstract Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken);
            }
            """);

        AssertNothingGenerated(result);
    }

    [Fact]
    public void Generates_registration_for_handler_inheriting_its_interface()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public interface IIntegrationEvent;

            public sealed record EntityAdded(int Id) : IIntegrationEvent;

            public class ForwardingHandler<T> : IOutboxMessageHandler<T> where T : IIntegrationEvent
            {
                public Task HandleAsync(T message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class EntityAddedHandler : ForwardingHandler<EntityAdded>;
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Generates_registration_for_handler_whose_base_is_in_a_referenced_assembly()
    {
        var result = GeneratorTestHelper.Run(
            source: """
                namespace Sample;

                public sealed record EntityAdded(int Id) : Shared.IIntegrationEvent;

                public sealed class EntityAddedHandler : Shared.ForwardingHandler<EntityAdded>;
                """,
            referencedSource: """
                using System.Threading;
                using System.Threading.Tasks;

                using Underground.Outbox;
                using Underground.Outbox.Data;

                namespace Shared;

                public interface IIntegrationEvent;

                public abstract class ForwardingHandler<T> : IOutboxMessageHandler<T> where T : IIntegrationEvent
                {
                    public Task HandleAsync(T message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
                }
                """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Inherits_lifetime_from_base_class()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Microsoft.Extensions.DependencyInjection;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            [MessageHandlerLifetime(ServiceLifetime.Scoped)]
            public abstract class ScopedHandler<T> : IOutboxMessageHandler<T>
            {
                public Task HandleAsync(T message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class TestMessageHandler : ScopedHandler<TestMessage>;
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Reports_concrete_base_and_subclass_handling_the_same_message_type()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            public class TestMessageHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public sealed class AuditedTestMessageHandler : TestMessageHandler;
            """);

        Snapshot.Validate(result);
    }

    [Fact]
    public void Ignores_open_generic_handler_classes()
    {
        var result = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(int Id);

            public class GenericHandler<T> : IOutboxMessageHandler<T>
            {
                public Task HandleAsync(T message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            public static class Outer<T>
            {
                public sealed class NestedHandler : IOutboxMessageHandler<TestMessage>
                {
                    public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
                }
            }
            """);

        AssertNothingGenerated(result);
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

        AssertNothingGenerated(result);
    }

    private static void AssertNothingGenerated(GeneratorDriverRunResult result)
    {
        Assert.Empty(result.GeneratedTrees);
        Assert.Empty(result.Diagnostics);
    }
}
