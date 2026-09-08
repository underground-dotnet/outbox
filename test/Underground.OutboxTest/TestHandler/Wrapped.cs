namespace Underground.OutboxTest.TestHandler;

// A generic message type: its C# name is Wrapped<T>, its runtime name Wrapped`1[[...]].
public record Wrapped<T>(T Body);
