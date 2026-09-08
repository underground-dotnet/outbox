namespace Underground.OutboxTest.TestHandler;

// A nested message type: its C# name is Envelope.Nested, its runtime name Envelope+Nested.
public static class Envelope
{
    public record Nested(int Id);
}
