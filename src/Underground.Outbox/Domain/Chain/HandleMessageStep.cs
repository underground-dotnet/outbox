namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// The remainder of the chain, as one stage sees it. Invoking it runs every stage inside this one and,
/// innermost, the dispatch; not invoking it means the message is never handed to its Handler.
/// </summary>
/// <param name="cancellationToken">
/// The token the rest of the chain runs under. It is a parameter rather than a capture so that a stage
/// can narrow it - a timeout, say - for everything inside it without affecting anything outside.
/// </param>
/// <returns>Whether the message was handled. See <see cref="IMessageStage{TEntity}"/>.</returns>
internal delegate Task<bool> HandleMessageStep(CancellationToken cancellationToken);
