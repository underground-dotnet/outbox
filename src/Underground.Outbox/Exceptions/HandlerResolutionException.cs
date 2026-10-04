namespace Underground.Outbox.Exceptions;

/// <summary>
/// Raised when the message's Handler could not be resolved from the container, typically because one of its
/// dependencies is not registered in this deployment. The container's error is the
/// <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class HandlerResolutionException : DeploymentGapException
{
    /// <summary>The Handler that could not be built.</summary>
    public HandlerType HandlerType { get; }

    /// <summary>Creates the exception. Called from generated code.</summary>
    /// <param name="handlerType">The Handler that could not be built.</param>
    /// <param name="messageId">The message it was resolved for.</param>
    /// <param name="innerException">The container's error.</param>
    public HandlerResolutionException(HandlerType handlerType, long messageId, Exception innerException)
        : base(messageId, $"The handler {handlerType.Name} for message {messageId} could not be resolved from the container in this deployment.", innerException)
    {
        HandlerType = handlerType;
    }
}
