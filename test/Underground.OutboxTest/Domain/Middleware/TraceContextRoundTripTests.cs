using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Configuration;
using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.OutboxTest.TestHandler;

namespace Underground.OutboxTest.Domain.Middleware;

/// <summary>
/// The one claim the middleware tests cannot make on their own: that the trace survives the database.
/// A message written under one Activity is handled, in a claim of its own, under a span of the same trace.
/// </summary>
[Collection("ExampleMessageHandler Collection")]
public class TraceContextRoundTripTests : DatabaseTest
{
    private readonly IServiceProvider _serviceProvider;

    public TraceContextRoundTripTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
        ExampleMessageHandler.CalledWith.Clear();
        ExampleMessageHandler.ObjectIds.Clear();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOutbox(cfg => cfg.AddHandler<ExampleMessageHandler, ExampleMessage>());
        serviceCollection.AddBaseServices(Database, testOutputHelper);

        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Fact]
    public async Task HandlingAMessageContinuesTheTraceOfTheTransactionThatWroteIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var context = CreateDbContext();
        var eventId = Guid.NewGuid();

        using var spans = new RecordingTracerProvider("Test.Producer");
        using var producer = new ActivitySource("Test.Producer");

        ActivityTraceId producerTraceId;
        ActivitySpanId producerSpanId;

        using (var writing = producer.StartActivity("write the message"))
        {
            producerTraceId = writing!.TraceId;
            producerSpanId = writing.SpanId;

            await context.AddMessagesAsync(
                _serviceProvider,
                [new OutboxMessage(eventId, DateTime.UtcNow, new ExampleMessage(1))],
                cancellationToken);
        }

        // the row carries the writing transaction's context, not a live ambient one
        var stored = await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.EventId == eventId, cancellationToken);
        Assert.NotNull(stored.TraceParent);
        Assert.Contains(producerTraceId.ToHexString(), stored.TraceParent, StringComparison.Ordinal);

        // handled with nothing ambient, exactly as a background worker does
        Assert.Null(Activity.Current);
        var processor = _serviceProvider.GetRequiredService<ConcurrentProcessor<TestDbContext, OutboxMessage>>();
        await processor.ProcessUntilIdleAsync(cancellationToken);

        var handled = spans.SpanFor(eventId);

        Assert.Equal(producerTraceId, handled.TraceId);
        Assert.Equal(producerSpanId, handled.ParentSpanId);
    }
}
