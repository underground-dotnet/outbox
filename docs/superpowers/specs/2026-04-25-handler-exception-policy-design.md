# Handler Exception Policy Design

## Summary

Replace runtime reflection over `DiscardOnAttribute` with registration-time handler exception policies.
The public API remains centered on `cfg.AddHandler<THandler>()`, but it returns a fluent builder that can attach exception handling rules to that handler registration.

Example:

```csharp
builder.Services.AddOutboxServices<AppDbContext>(cfg =>
{
    cfg.AddHandler<ExampleMessageHandler>()
       .OnException<DataException>().Discard();
});
```

This design removes the remaining reflection from `DiscardMessageOnExceptionHandler`, keeps inbox and outbox registration separate through the existing `AddInboxServices` and `AddOutboxServices` scopes, and creates a reusable fluent registration surface for future handler behaviors.

## Goals

- Remove runtime reflection used to read `DiscardOnAttribute`.
- Keep handler registration explicit and scoped to inbox or outbox configuration.
- Support multiple exception rules per handler.
- Match derived exception types.
- Resolve overlapping rules by choosing the most specific matching exception type.
- Preserve current fallback behavior when no rule matches: do not discard, and let the existing retry path continue unchanged.
- Create an extensible registration model that can host future handler-specific options beyond exception handling.

## Non-Goals

- Preserve the `DiscardOnAttribute` API.
- Move exception handling logic into the source generator.
- Redesign the processor retry model.
- Introduce separate `AddOutboxHandler` or `AddInboxHandler` APIs.

## Public API

`ServiceConfiguration.AddHandler<THandler>()` returns a handler registration builder instead of `ServiceConfiguration`.

Example:

```csharp
cfg.AddHandler<ExampleMessageHandler>()
   .OnException<DataException>().Discard()
   .OnException<TimeoutException>().RetryLater();
```

### Proposed fluent shape

- `AddHandler<THandler>()`
- `AddHandler<THandler>(ServiceLifetime lifetime)`
- `OnException<TException>()`
- Terminal actions such as:
  - `Discard()`
  - `RetryLater()` in the future
  - other actions later without changing `AddHandler`

### Chaining model

Terminal actions return the handler builder so additional exception rules can be added on the same registration:

```csharp
cfg.AddHandler<MyHandler>()
   .OnException<DataException>().Discard()
   .OnException<TimeoutException>().RetryLater();
```

This keeps `AddHandler` readable while leaving room for future fluent capabilities unrelated to exception handling.

## Internal Model

Replace `ServiceConfiguration.HandlersWithLifetime : List<ServiceDescriptor>` with a richer registration model.

### HandlerRegistration

Each handler registration should contain:

- `ServiceType`
  The discovered handler interface, such as `IOutboxMessageHandler<ExampleMessage>`.
- `ImplementationType`
  The concrete handler type passed to `AddHandler<THandler>()`.
- `ServiceLifetime`
  The chosen DI lifetime.
- `ExceptionPolicies`
  A list of configured exception rules for this handler registration.

### ExceptionPolicyRule

Each rule should contain:

- `ExceptionType`
- `Action`

`Action` should be modeled as an enum or small value object so future actions can be added without changing the registry structure.

## Runtime Architecture

### Registration phase

During `AddOutboxServices` and `AddInboxServices`:

1. `cfg.AddHandler<THandler>()` creates a `HandlerRegistration`.
2. The registration builder appends exception policy rules to that registration.
3. Setup code registers the handler service descriptor into DI.
4. Setup code also registers a read-only exception policy registry built from all handler registrations in that service configuration.

### Processing phase

1. The generated dispatcher continues to invoke the resolved handler and wrap failures in `MessageHandlerException`.
2. `MessageHandlerException` continues to carry the concrete handler type.
3. `ProcessExceptionFromHandler` invokes registered runtime exception handlers.
4. `DiscardMessageOnExceptionHandler` uses the policy registry instead of reflection to look up rules for `ex.HandlerType`.
5. If the winning rule action is `Discard`, the message is deleted.
6. If no rule matches, the discard handler does nothing and existing retry behavior remains unchanged.

This keeps the source generator focused on dispatching and keeps policy evaluation in the runtime exception handling pipeline.

## Matching Semantics

### Basic matching

A rule matches when:

```csharp
rule.ExceptionType.IsAssignableFrom(ex.InnerException.GetType())
```

This preserves current behavior where a configured base exception type also matches subclasses.

### Conflict resolution

Multiple rules may match the same thrown exception. The winning rule is the most specific matching exception type.

Example:

- `OnException<Exception>().Discard()`
- `OnException<DataException>().RetryLater()`

For a thrown `SqlException : DbException : Exception`, the closest matching configured exception type wins.

### Specificity algorithm

Use inheritance distance from the thrown exception type to the configured exception type:

- exact match distance `0`
- direct base type distance `1`
- and so on

Choose the rule with the smallest distance.

If two rules have the same distance, configuration should be rejected during startup as ambiguous. This avoids silently picking one of two equally specific rules.

## Error Handling

### Unknown handler policy

If a `MessageHandlerException.HandlerType` is not present in the registry, the exception handler should behave as if no policy was configured.

### Invalid registration

Startup validation should fail when:

- `AddHandler<THandler>()` does not resolve to exactly one handler interface for the current configuration scope.
- a configured exception rule does not use an `Exception` type.
- the same effective exception specificity produces ambiguous actions for one handler.

### Backward compatibility

`DiscardOnAttribute` should be removed from public guidance and from runtime usage.
It can either be deleted immediately or kept temporarily as obsolete API during migration, but runtime behavior must no longer depend on it.

## Testing

Add tests for:

- fluent registration stores exception rules on the correct handler registration
- `DiscardMessageOnExceptionHandler` discards without reflection when an exact exception matches
- derived exceptions match configured base exception rules
- the most specific rule wins over broader matches
- no matching rule preserves current retry behavior
- handler registration remains unambiguous across `AddOutboxServices` and `AddInboxServices`
- invalid ambiguous rule sets fail during startup validation

## Implementation Notes

- Keep the source generator changes minimal. It should still only need to throw `MessageHandlerException(handler.GetType(), ...)`.
- The registry should be immutable after service setup.
- The fluent builder types should be small and internal unless there is a strong reason to expose them publicly.
- Future handler behaviors should extend `HandlerRegistration` rather than reintroducing attributes.

## Migration

Current:

```csharp
public class DiscardFailedMessageHandler : IOutboxMessageHandler<DiscardMessage>
{
    [DiscardOn(typeof(DataException))]
    public Task HandleAsync(DiscardMessage message, MessageMetadata metadata, CancellationToken cancellationToken)
    {
        throw new DataException("Failed to handle message");
    }
}
```

Target:

```csharp
cfg.AddHandler<DiscardFailedMessageHandler>()
   .OnException<DataException>().Discard();
```

This moves exception policy from handler implementation details into service registration, which is the correct place for inbox/outbox-specific composition concerns.
