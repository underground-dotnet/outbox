# DiscardOn Generator Refactor Design

## Summary

Refactor `DiscardMessageOnExceptionHandler<TEntity>` so it no longer uses reflection to inspect `DiscardOnAttribute` at runtime. Instead, the source generator will parse `DiscardOnAttribute` declarations from handler `HandleAsync` methods, generate a handler-to-exception mapping keyed by concrete handler type, and register that generated mapper in dependency injection through the already generated `ConfigureOutboxServices` class.

This behavior applies to both inbox and outbox handlers.

## Goals

- Remove runtime reflection from `DiscardMessageOnExceptionHandler<TEntity>`.
- Preserve the existing `DiscardOnAttribute` authoring model on handler `HandleAsync` methods.
- Support both `IInboxMessageHandler<T>` and `IOutboxMessageHandler<T>`.
- Generate discard-on metadata for local handlers and referenced handler assemblies discovered through `[ContainsOutboxHandlers]`.
- Register the generated mapping service automatically through generated DI code.

## Non-Goals

- Changing how messages are deleted after a discard decision is made.
- Moving discard handling into generated dispatchers.
- Changing `DiscardOnAttribute` usage from method-level to type-level.
- Introducing separate discard mapping services for inbox and outbox.

## Current State

Today `DiscardMessageOnExceptionHandler<TEntity>` reflects over `ex.HandlerType`, finds a `HandleAsync` method, reads `DiscardOnAttribute`, and checks whether `ex.InnerException` matches one of the declared exception types. This has two problems:

- The discard policy is discovered at runtime via reflection.
- The runtime lookup is disconnected from the source generator, even though the generator already discovers handlers and generates DI extensions.

## Proposed Design

### Runtime contract

Add a runtime interface to the outbox library:

```csharp
public interface IDiscardOnExceptionMapping
{
    IReadOnlySet<Type>? GetDiscardOnTypes(Type handlerType);
}
```

This interface is keyed by the concrete handler implementation type. That matches the existing `MessageHandlerException(handler.GetType(), ...)` flow and avoids ambiguity if one class implements multiple handler interfaces.

### Runtime exception handling flow

`DiscardMessageOnExceptionHandler<TEntity>` will accept `IDiscardOnExceptionMapping` through DI and perform the following flow:

1. Read discardable exception types using `mapping.GetDiscardOnTypes(ex.HandlerType)`.
2. Return immediately if the mapping has no entry for the handler.
3. Check whether any configured exception type `IsInstanceOfType(ex.InnerException)`.
4. If matched, log and delete the message as today.
5. Otherwise do nothing.

`ProcessExceptionFromHandler<TEntity>` remains unchanged.

### Generator responsibilities

The source generator will extend handler discovery so each discovered handler carries discard metadata in addition to handler kind and message type.

For each discovered concrete handler:

1. Identify the concrete `HandleAsync` method that implements the handler interface.
2. Inspect that method for `DiscardOnAttribute`.
3. Read the constructor arguments and resolve all declared exception type symbols.
4. Store fully qualified exception type names in the handler model.

This applies to:

- Local handlers discovered through the syntax provider.
- External handlers discovered through metadata references in assemblies marked with `[ContainsOutboxHandlers]`.

Handlers without `DiscardOnAttribute` are omitted from the generated map.

### Generated code

The generator will emit a concrete implementation:

```csharp
public sealed class GeneratedDiscardOnExceptionMapping : IDiscardOnExceptionMapping
{
    public IReadOnlySet<Type>? GetDiscardOnTypes(Type handlerType)
    {
        if (handlerType == typeof(MyHandler))
        {
            return new HashSet<Type>
            {
                typeof(MyException),
                typeof(AnotherException),
            };
        }

        return null;
    }
}
```

The mapping contains entries for both inbox and outbox handlers because the runtime lookup is based only on the concrete handler type.

### Dependency injection

The already generated `ConfigureOutboxServices` class will also register the generated mapping:

- `services.AddScoped<IDiscardOnExceptionMapping, GeneratedDiscardOnExceptionMapping>();`

This registration will be emitted for both:

- `AddOutboxServices<TContext>()`
- `AddInboxServices<TContext>()`

No manual registration is required by consumers.

## Data Model Changes

`HandlerClassInfo` should be extended to include discard exception type names, for example as an immutable collection of strings. The handler model then becomes the single source of truth for:

- handler full name
- message type full name
- handler kind
- discard-on exception types

This keeps dispatcher generation and discard mapping generation aligned on the same discovered handler set.

## Diagnostics

The generator should not guess if handler method resolution is ambiguous. It should emit a diagnostic when it cannot reliably identify the correct `HandleAsync` implementation for a discovered handler. This keeps generation deterministic and prevents silent misconfiguration.

## Behavior Details

- `DiscardOnAttribute` continues to be read only from `HandleAsync`.
- Runtime matching remains assignable, not exact: `configuredType.IsInstanceOfType(ex.InnerException)`.
- Derived exceptions should therefore continue to trigger discard when their base type is declared.
- Handlers with no discard configuration produce no mapping entry rather than an empty set.

## Testing

### Generator tests

- Generates a discard mapping entry for an outbox handler with `[DiscardOn]`.
- Generates a discard mapping entry for an inbox handler with `[DiscardOn]`.
- Omits handlers without `[DiscardOn]`.
- Includes discard mappings for referenced handlers from assemblies marked with `[ContainsOutboxHandlers]`.

### Runtime tests

- Deletes the message when the thrown exception matches a configured discard type.
- Deletes the message when the thrown exception derives from a configured discard type.
- Does not delete when the handler has no mapping entry.
- Does not delete when the exception type is not mapped.

## Risks and Constraints

- External handler metadata parsing must work from symbols loaded from referenced assemblies, not only syntax trees.
- Returning a new `HashSet<Type>` per call is simple but allocates; acceptable initially, and can be optimized later if needed.
- The DI registration should remain safe when both inbox and outbox are configured in the same application.

## Implementation Outline

1. Add `IDiscardOnExceptionMapping` to the runtime library.
2. Refactor `DiscardMessageOnExceptionHandler<TEntity>` to use the interface instead of reflection.
3. Extend generator handler discovery to capture `DiscardOnAttribute` exception types from `HandleAsync`.
4. Generate `GeneratedDiscardOnExceptionMapping`.
5. Update generated `ConfigureOutboxServices` output to register the generated mapper.
6. Add generator and runtime tests covering inbox and outbox behavior.
