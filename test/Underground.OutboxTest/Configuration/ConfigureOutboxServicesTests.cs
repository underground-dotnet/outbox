// using Microsoft.Extensions.DependencyInjection;

// using Underground.Outbox;
// using Underground.Outbox.Configuration;
// using Underground.OutboxTest.TestHandler;

// namespace Underground.OutboxTest.Configuration;

// public class ConfigureOutboxServicesTests : DatabaseTest
// {
//     private readonly ITestOutputHelper _testOutputHelper;

//     public ConfigureOutboxServicesTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
//     {
//         _testOutputHelper = testOutputHelper;
//     }

//     TODO: should be cannot, multiple handlers is not supported
//     [Fact]
//     public void CanAddMultipleHandlersForSameMessageType()
//     {
//         // Arrange
//         var serviceCollection = new ServiceCollection();
//         serviceCollection.AddBaseServices(Container, _testOutputHelper);

//         serviceCollection.AddOutboxServices<TestDbContext>(cfg =>
//         {
//             cfg.AddHandler<ExampleMessageHandler>();
//             cfg.AddHandler<ExampleMessageAnotherHandler>(); // Adding a second handler for same message type
//         });

//         // Act
//         var serviceProvider = serviceCollection.BuildServiceProvider();
//         var handlers = serviceProvider.GetServices<IOutboxMessageHandler<ExampleMessage>>();

//         // Assert
//         Assert.Equal(2, handlers.Count());
//     }
// }
