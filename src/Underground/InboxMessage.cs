using System;
using System.Text.Json;

namespace Underground;

public class InboxMessage(Guid id, DateTime receivedOn, object data)
{
    public Guid Id { get; init; } = id;
    public DateTime ReceivedOn { get; init; } = receivedOn;
    public string Type { get; init; } = data.GetType().AssemblyQualifiedName!;
    public string Data { get; init; } = JsonSerializer.Serialize(data);
}
