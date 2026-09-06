namespace Underground.Outbox.Domain.Middleware;

/// <summary>
/// The remainder of the pipeline, as one middleware sees it. Not invoking it means the message is never handed to
/// its Handler.
/// </summary>
/// <param name="cancellationToken">
/// The token the rest of the pipeline runs under. A parameter rather than a capture, so a middleware can narrow
/// it for everything inside without affecting anything outside.
/// </param>
/// <returns>What became of the message. See <see cref="IMessageMiddleware{TEntity}"/>.</returns>
internal delegate Task<ProcessingAttempt> MessageMiddlewareDelegate(CancellationToken cancellationToken);
