using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Underground.Outbox.Configuration;

public class OutboxServiceConfiguration
{
    public string SchemaName { get; set; } = "public";

    public string TableName { get; set; } = $"outbox";

    internal string FullTableName => $"\"{SchemaName}\".\"{TableName}\"";

    public bool CreateSchemaAutomatically { get; set; } = true;

    public int BatchSize { get; set; } = 5;

    public int ParallelProcessingOfPartitions { get; set; } = 4;

    internal List<ServiceDescriptor> HandlersWithLifetime = [];

    internal Type? DbContextType { get; set; }

    public OutboxServiceConfiguration UseDbContext<TDbContext>() where TDbContext : DbContext
    {
        DbContextType = typeof(TDbContext);
        return this;
    }

    public OutboxServiceConfiguration AddHandler<TMessageHandlerType>()
    {
        return AddHandler(typeof(TMessageHandlerType));
    }

    public OutboxServiceConfiguration AddHandler(HandlerType messageHandlerType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        var interfaceType = messageHandlerType.GetInterface("Underground.Outbox.IOutboxMessageHandler`1");

        if (interfaceType?.IsGenericType == true)
        {
            Console.WriteLine($"Added handler for {interfaceType} with {messageHandlerType} ");
            HandlersWithLifetime.Add(new ServiceDescriptor(interfaceType, messageHandlerType, serviceLifetime));
        }

        return this;
    }
}
