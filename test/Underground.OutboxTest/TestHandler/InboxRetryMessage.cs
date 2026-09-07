namespace Underground.OutboxTest.TestHandler;

/// <summary>An inbox message whose handler counts how many times it ran. See <see cref="InboxRetryMessageHandler"/>.</summary>
public record InboxRetryMessage(int Id);
