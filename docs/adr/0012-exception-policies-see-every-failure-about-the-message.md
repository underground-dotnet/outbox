# Exception Policies see every failure about the message, matched on its cause

Exception Policies used to see only exceptions thrown inside a Handler, wrapped in
`MessageHandlerException`. Every other failure of a Processing Attempt, such as a payload that is not
valid JSON, a timeout, or the save after the Handler, was retried with no policy consulted. So
`OnException<JsonException>().Discard()` silently did nothing.

Now a policy sees every failure about the message: its payload, finding or building its Handler, the
Handler itself, its timeout, and the save of what it wrote. Failures of this library's own bookkeeping
writes are excluded, because they say nothing about the message. A policy matches the cause, meaning the
exception that actually went wrong, and `IMessageExceptionHandler` receives that exception directly.
`MessageHandlerException` and `ParsingException` are gone. A JSON `null` body throws `JsonException`, so
one policy covers every unreadable payload. Policies on a Handler apply whenever the message's Handler is
known, including when the failure was outside its code.

A Deployment Gap is the exception to broad matching. That covers an unknown type
(`UnknownMessageTypeException`) and a Handler that cannot be built (`HandlerResolutionException`).
Both derive from `DeploymentGapException`, and they match only a policy whose exception type is
`DeploymentGapException` or one of its subclasses. During a rolling deploy a newer producer can write a
type an older worker does not know yet. A blanket `OnException<Exception>().Discard()` would drop messages
the next deployment could have handled. Retiring a message type for good is still possible by naming the
exception on purpose.

## Considered Options

- A separate global hook for failures outside the Handler. Rejected: the global level of policies already
  exists, and a second hook would mean two places to look and two precedence rules.
- Matching on library wrappers (where the failure happened) rather than on the cause. Rejected: users name
  the exception they can see in their logs.
- Leaving unknown types out of policies entirely. Rejected: it left no way to drop the messages of a type
  that was retired on purpose.

## Consequences

Breaking for anyone who implemented `IMessageExceptionHandler` or caught `ParsingException`.

A `JsonException` thrown inside a Handler and one from reading the payload match the same policy. That is
accepted, because both mean the payload cannot be read.

An exception handler that itself throws still escapes the pipeline, which on the inbox rolls back the
recorded retry. Exception handlers are expected to be simple enough not to fail. Changing that is outside
this decision.
