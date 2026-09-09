using VerifyXunit;

namespace Underground.Outbox.SourceGeneratorTest;

public class OutboxGeneratorTest
{
    /// <summary>
    /// The context declarations every case shares. A real <see cref="Microsoft.EntityFrameworkCore.DbContext"/>,
    /// because the binding attribute's type parameter is constrained to one.
    /// </summary>
    private const string Contexts = """
        using Microsoft.EntityFrameworkCore;

        namespace Sample;

        public sealed class OrdersContext : DbContext;

        public sealed class BillingContext : DbContext;
        """;

    [Fact]
    public Task Generates_nothing_to_register_without_handlers()
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
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record TestMessage(string Text);

            [OutboxHandler<OrdersContext>]
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
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record InboxMessageType(int Id);

            public sealed record OutboxMessageType(int Id);

            [InboxHandler<OrdersContext>]
            public sealed class InboxHandler : IInboxMessageHandler<InboxMessageType>
            {
                public Task HandleAsync(InboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            [OutboxHandler<OrdersContext>]
            public sealed class OutboxHandler : Underground.Outbox.IOutboxMessageHandler<OutboxMessageType>
            {
                public Task HandleAsync(OutboxMessageType message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Generates_a_dispatcher_per_context_when_two_contexts_handle_one_message_type()
    {
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record SharedContract(string Text);

            [OutboxHandler<OrdersContext>]
            public sealed class OrdersHandler : IOutboxMessageHandler<SharedContract>
            {
                public Task HandleAsync(SharedContract message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            [OutboxHandler<BillingContext>]
            public sealed class BillingHandler : IOutboxMessageHandler<SharedContract>
            {
                public Task HandleAsync(SharedContract message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Generates_dispatcher_for_nested_message_type()
    {
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public static class Outer
            {
                public sealed record Inner(string Text);
            }

            [OutboxHandler<OrdersContext>]
            public sealed class InnerHandler : IOutboxMessageHandler<Outer.Inner>
            {
                public Task HandleAsync(Outer.Inner message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Generates_dispatcher_for_generic_message_type()
    {
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record Payload(string Text);

            public sealed record Envelope<T>(T Body);

            [OutboxHandler<OrdersContext>]
            public sealed class EnvelopeHandler : IOutboxMessageHandler<Envelope<Payload>>
            {
                public Task HandleAsync(Envelope<Payload> message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Reports_competing_handlers_bound_to_the_same_context()
    {
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record TestMessage(string Text);

            [OutboxHandler<OrdersContext>]
            public sealed class FirstHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            [OutboxHandler<OrdersContext>]
            public sealed class SecondHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Reports_a_handler_that_names_no_context()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Data;

            namespace Sample;

            public sealed record TestMessage(string Text);

            public sealed class UnboundHandler : IOutboxMessageHandler<TestMessage>
            {
                public Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }

    [Fact]
    public Task Ignores_abstract_handler_classes()
    {
        var driver = GeneratorTestHelper.Run($$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record TestMessage(int Id);

            [OutboxHandler<OrdersContext>]
            public abstract class AbstractHandler : IOutboxMessageHandler<TestMessage>
            {
                public abstract Task HandleAsync(TestMessage message, MessageMetadata metadata, CancellationToken cancellationToken);
            }
            """);

        return Verify(driver);
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

        return Verify(driver);
    }

    /// <summary>
    /// Two modules may each call their context <c>AppDbContext</c>. Naming both registration methods after
    /// the simple name would emit overloads that differ only in a lambda parameter, which every call site
    /// resolves as ambiguous - so the full name is used for both.
    /// </summary>
    [Fact]
    public Task Names_registrations_by_full_context_name_when_two_contexts_share_a_simple_name()
    {
        var driver = GeneratorTestHelper.Run("""
            using System.Threading;
            using System.Threading.Tasks;

            using Microsoft.EntityFrameworkCore;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            namespace Orders;

            public sealed class AppDbContext : DbContext;

            public sealed record OrderPlaced(int Id);

            [OutboxHandler<AppDbContext>]
            public sealed class OrderHandler : IOutboxMessageHandler<OrderPlaced>
            {
                public Task HandleAsync(OrderPlaced message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            namespace Billing
            {
                public sealed class AppDbContext : DbContext;

                public sealed record InvoiceRaised(int Id);

                [OutboxHandler<AppDbContext>]
                public sealed class InvoiceHandler : IOutboxMessageHandler<InvoiceRaised>
                {
                    public Task HandleAsync(InvoiceRaised message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
                }
            }
            """);

        return Verify(driver);
    }

    /// <summary>
    /// A referenced assembly's Handlers land under the context they name, not under the referencing
    /// assembly's - which is what keeps cross-assembly discovery safe now that grouping is explicit.
    /// </summary>
    [Fact]
    public Task Groups_a_referenced_assemblys_handler_under_its_own_context()
    {
        var driver = GeneratorTestHelper.Run(
            $$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            {{Contexts}}

            public sealed record LocalMessage(string Text);

            [OutboxHandler<OrdersContext>]
            public sealed class LocalHandler : IOutboxMessageHandler<LocalMessage>
            {
                public Task HandleAsync(LocalMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            """
            using System.Threading;
            using System.Threading.Tasks;

            using Microsoft.EntityFrameworkCore;

            using Underground.Outbox;
            using Underground.Outbox.Attributes;
            using Underground.Outbox.Data;

            [assembly: ContainsOutboxHandlers]

            namespace Shared;

            public sealed class ShippingContext : DbContext;

            public sealed record ShippedMessage(string Text);

            [OutboxHandler<ShippingContext>]
            public sealed class ShippedHandler : IOutboxMessageHandler<ShippedMessage>
            {
                public Task HandleAsync(ShippedMessage message, MessageMetadata metadata, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        return Verify(driver);
    }
}
