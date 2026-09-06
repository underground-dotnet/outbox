namespace Underground.Outbox.Domain.Chain;

/// <summary>
/// The remainder of the chain, as one stage sees it. Not invoking it means the message is never handed to
/// its Handler.
/// </summary>
/// <param name="cancellationToken">
/// The token the rest of the chain runs under. A parameter rather than a capture, so a stage can narrow
/// it for everything inside without affecting anything outside.
/// </param>
/// <returns>What became of the message. See <see cref="IMessageStage{TEntity}"/>.</returns>
internal delegate Task<Attempt> HandleMessageStep(CancellationToken cancellationToken);
