Status: ready-for-agent

# Exception Policies see every failure about the message

See ADR 0012 and the **Exception Policy** and **Deployment Gap** entries in `CONTEXT.md`.

## Problem Statement

An application author configures Exception Policies, such as `OnException<JsonException>().Discard()`,
expecting them to decide what happens to a message that keeps failing. Today a policy is consulted only
when the Handler itself throws. Every other failure of a Processing Attempt is retried forever with no
policy consulted, and so blocks its Group (ADR 0004):

- a payload that is not valid JSON, or is the JSON literal `null`
- a message whose stored type no Handler claims
- a Handler that cannot be built from the container
- a Handler that overran its timeout
- a failure saving what the Handler wrote

The policy silently does nothing. The author cannot drop a message they know can never be read, and
nothing tells them their policy will never fire.

## Solution

Every failure of a Processing Attempt that concerns the message itself goes through the same Exception
Policies, at the same two levels (on the Handler and for the whole inbox or outbox) with the same
precedence. A policy matches the cause, meaning the exception that actually went wrong, so
`OnException<JsonException>()` now fires for an unreadable payload. A custom exception handler receives
that cause directly.

A Deployment Gap is the exception: an unknown message type, or a Handler that cannot be built. It matches
only a policy that names a Deployment Gap exception on purpose, so a broad `OnException<Exception>()`
cannot discard messages that the next deployment could have handled. Failures of the library's own
bookkeeping writes reach no policy, as today.

## User Stories

1. As an application author, I want `OnException<JsonException>().Discard()` to drop a message whose payload is not valid JSON, so that one corrupt row does not block its Group forever.
2. As an application author, I want a message whose payload is the JSON literal `null` to fail with `JsonException`, so that one policy covers every unreadable payload.
3. As an application author, I want a payload failure to match the policies on the message's Handler, so that a `ForHandler<…>()` chain stays a complete override for that message type.
4. As an application author, I want a payload failure with no matching policy to be retried with backoff as today, so that adding this feature changes nothing for authors who configure no policies.
5. As an application author, I want a Handler that overran its timeout to reach my policies as `HandlerTimeoutException`, so that I can decide what a timeout means for that Handler.
6. As an application author, I want `OnException<TimeoutException>()` to match a Handler timeout, because `HandlerTimeoutException` is a `TimeoutException` and policies match the nearest base type like a `catch` block.
7. As an application author, I want a failure saving what my Handler wrote to reach my policies with the database's own exception, so that I can match it with the exception I see in my logs.
8. As an application author, I want an exception my Handler throws to match my policies exactly as it does today, so that existing policies keep their meaning.
9. As an application author, I want my custom `IMessageExceptionHandler` to receive the exception that actually went wrong, so that I do not have to unwrap `InnerException`.
10. As an application author, I want a message whose type no Handler claims to keep retrying by default, so that during a rolling deploy an older worker does not lose messages written by a newer producer.
11. As an application author, I want a broad `OnException<Exception>().Discard()` to skip a Deployment Gap, so that a catch-all policy cannot silently drop messages that the next deployment could handle.
12. As an application author retiring a message type for good, I want `OnException<UnknownMessageTypeException>().Discard()` to drop its leftover messages, so that I do not have to delete rows by hand.
13. As an application author, I want `OnException<DeploymentGapException>()` to match both kinds of Deployment Gap, so that one policy covers them when I mean to.
14. As an application author, I want a Handler whose constructor dependency is not registered to keep retrying, so that a configuration mistake does not discard every message of its type before I fix it.
15. As an application author, I want a Handler that cannot be built to fail with `HandlerResolutionException` wrapping the container's error, so that I can tell it apart from an `InvalidOperationException` my Handler throws.
16. As an application author, I want a policy on a Handler to apply when that Handler cannot be built, as long as the policy names a Deployment Gap exception, so that I can retire one Handler's messages without a global policy.
17. As an application author, I want an unknown message type to match only global policies, because no Handler claims it.
18. As an application author, I want exactly one policy to run for any failure, as today, so that precedence stays predictable whatever the failure's source.
19. As an application author, I want the library's own bookkeeping write failures never to reach my policies, so that a policy cannot act on a message whose outcome was never recorded.
20. As an application author, I want every failure to be recorded as a failed Processing Attempt before any policy runs, so that the retry and backoff happen whichever policy matches.
21. As an application author, I want a failure that reaches no policy to be logged in the existing outcome line, so that I can see why a Group stopped draining.
22. As an operator, I want a Deployment Gap's exception type in the logs to say that a deployment is missing something, so that I fix the deployment rather than the data.
23. As an application author upgrading, I want the README and the release to say that `IMessageExceptionHandler` now takes `Exception` and that `MessageHandlerException` and `ParsingException` are gone, so that I know what to change.
24. As an application author using the inbox, I want all of the above to behave the same on the inbox as on the outbox, so that I do not have to learn two rule sets.
25. As an application author using the inbox, I want a discarded payload failure's row to be gone after the inbox transaction commits, so that Discard means the same on both sides.

## Implementation Decisions

- **Which failures reach policies.** Any failure inside the Middleware Pipeline other than the library's
  own bookkeeping writes (scheduling the retry, marking Completed). That covers reading the payload,
  looking up the Handler Entry, resolving the Handler from the scope, running the Handler, the Handler's
  timeout, and the save after the Handler returns. A shutdown cancellation still travels through
  unrecorded, as today.
- **Matching on the cause.** Policy selection matches the type of the exception that actually went wrong,
  walking base types nearest first. The per-handler level comes before the global level, and the first
  match wins, as today.
- **Finding the Handler for selection.** Selection no longer reads the Handler from a wrapper exception.
  It looks up the Handler Entry in the Handler Registry by the message's stored type. If an entry is
  found, the per-handler policies registered for that Handler and message type are consulted first. If no
  entry is found (an unknown type), only global policies are consulted.
- **Deployment Gap matching rule.** If the cause is a `DeploymentGapException`, only policies whose
  exception type is `DeploymentGapException` or a subclass of it are candidates, at either level. Broader
  policies (`Exception`, `InvalidOperationException`, …) are skipped for it.
- **New public exceptions.**
  - `DeploymentGapException`: abstract, the base of both Deployment Gaps.
  - `UnknownMessageTypeException`: thrown when no Handler Entry claims the stored type. It carries the
    stored type name and the message id.
  - `HandlerResolutionException`: thrown when the Handler cannot be resolved from the scope. It wraps
    the container's exception as `InnerException` and carries the Handler type.
  - Every message an exception carries names the message by id only, never its payload, because these
    messages end up in logs and on spans.
- **Removed public types.** `MessageHandlerException` and `ParsingException` are removed.
- **`IMessageExceptionHandler.HandleAsync` signature.** The first parameter changes from
  `MessageHandlerException` to `Exception`, which is the cause. The other parameters are unchanged. The
  built-in Discard handler and the test policies are updated to match.
- **Generated Handler Entry.** The source generator no longer wraps the Handler's exception. A
  deserialized `null` payload throws `JsonException`, and malformed JSON throws its own `JsonException`
  as before. Resolving the Handler from the scope is wrapped so that a container failure becomes
  `HandlerResolutionException`. The shutdown-versus-own-cancellation filter that exists today stays
  wherever it is still needed, so that a Handler's own cancellation (for example an HttpClient timeout)
  is still an ordinary failure.
- **Dispatcher.** An unknown stored type throws `UnknownMessageTypeException` in place of
  `ParsingException`.
- **Recording failures.** The failure middleware keeps its order: clear the change tracker, write the
  guarded retry, and if the Lease was lost return without consulting policies. Only then does it run
  policy selection for every failure, not only Handler failures. Nothing about the pipeline order
  changes.
- **Inbox and outbox.** Both pipelines share the failure middleware and selection, so no per-side logic is
  added.
- **Docs.** Update the README's Error handling section: which failures reach policies, matching on the
  cause, the Deployment Gap rule with an example of retiring a type, and the breaking changes. Update
  the example app if a policy there changes meaning (it has `OnException<TimeoutException>()`, which will
  now also match a Handler timeout).

## Testing Decisions

- A good test configures services and policies, writes messages, runs the processor until idle, and
  then asserts on the table: the row is gone (discarded), or it remains with `retry_count` increased and
  the message behind it in the Group unhandled (retried and blocking). Tests do not assert on which
  middleware or class did the work.
- **Seam 1: the processor end to end.** This is the same seam `ProcessorErrorTests` uses, on Testcontainers
  Postgres, and covers almost every behaviour.
  - Malformed or `null` payloads and unknown types: add a valid message, then rewrite its `data` or
    `type` column with raw SQL before processing.
  - A Handler that cannot be built: a test Handler with a constructor dependency that is never
    registered.
  - A timeout: follow `HandlerTimeoutTests`, with a short `HandlerTimeout` and a Handler that honours its
    token.
  - A failing save after the Handler: a Handler that stages an entity violating a database constraint.
  - Each behaviour that could differ between sides is tested on both the inbox and the outbox, following
    `HandlerCancellationTests` and the inbox tests.
- Cases to cover, at least:
  - `JsonException` discard (global and per-handler) for malformed and for `null` payloads.
  - No policy: a payload failure is retried and blocks the Group.
  - `OnException<Exception>().Discard()` does not discard an unknown type or an unbuildable Handler.
  - `OnException<UnknownMessageTypeException>()` and `OnException<DeploymentGapException>()` do discard
    an unknown type.
  - A per-handler Deployment Gap policy applies to an unbuildable Handler.
  - A timeout reaches a `TimeoutException` policy.
  - A save failure reaches its policy.
  - The existing precedence tests (handler over global, nearest type, one policy only) keep passing.
  - A custom exception handler receives the cause and not a wrapper.
- **Seam 2: the source generator snapshots.** The generated Handler Entry changes, so accept the updated
  `.verified.cs` snapshots after reviewing the diff. No new generator behaviour tests are needed.
- No unit tests on policy selection or middleware internals.

## Out of Scope

- An exception handler that itself throws. It still escapes the pipeline, and on the inbox this rolls back
  the recorded retry. Exception handlers are expected to be simple enough not to fail.
- Dead-lettering. ADR 0004 names it as a follow-up, and a `DeadLetter()` policy can later sit beside
  `Discard()`.
- New policy actions beyond `Discard()`.
- Any change to backoff, retry counting, or the ordering guarantees.

## Further Notes

- This is a breaking change to a package published on NuGet. Version it accordingly and call it out in
  the release notes.
- A `JsonException` thrown inside a Handler matches the same policy as an unreadable payload. That is
  accepted (ADR 0012).
