namespace Underground.OutboxTest.TestHandler;

/// <summary>An outbox message whose handler counts how many times it ran. See <see cref="RetryMessageHandler"/>.</summary>
public record RetryMessage(int Id);
