# Outboxes are bound to a `DbContext`, and handlers name theirs

An application may register several inboxes and outboxes, one pair per `DbContext`, so that a
modular monolith can give each module its own message tables in its own schema without giving it
its own process. The library carries a message from a module to itself — written in one
transaction, handled by that module's worker — and never between modules. A handler that wants to
reach a neighbour calls it, by whatever in-process means the application already has, and the
neighbour writes to its own inbox. There is no transport here and no relay, and adding several
outboxes does not add one.

The part worth recording is why a handler carries `[OutboxHandler<OrdersContext>]` rather than
being discovered from the project it lives in. The generated dispatcher ends every branch in a
container lookup for `IOutboxMessageHandler<TMessage>`, and handler registrations were
`TryAddEnumerable`, which dedupes on implementation type: two modules handling the same contract
type both register, and `GetRequiredService` returns whichever was registered last. The wrong
module's handler runs, successfully, on the right module's message. Fixing that means keying
resolution on the context type, which means the generator must know the key at compile time.
Grouping by assembly instead would make correctness a property of project layout and would break
the cross-assembly discovery `[ContainsOutboxHandlers]` exists for; reading the key off the
`AddOutboxServices<T>(cfg => cfg.AddHandler<H, M>())` call would make the generator sensitive to
how that call is spelled — a loop or a helper defeats it. The attribute states the binding where
the binding is, on the handler.

## Consequences

This is a breaking major version, and one change rather than three: `IOutbox<TContext>` and its
neighbours grow a generic parameter, handlers must be attributed (`OUTBOX002`), and the schema
becomes required (ADR 0007). Registrations are keyed on `typeof(TContext)` and the generator emits
an `internal` dispatcher per context, so two identically-named generated types can never meet.
`OUTBOX001` becomes per-context: two modules may handle the same message type, one module may not.

Wrapping a neighbour's contract in a module-local type is no longer necessary to keep handlers
apart, and remains worth doing for a different reason. The `type` column stores the handled type's
`FullName`, so an unwrapped message persists a *neighbour's* type name; when they rename or move
it, rows already written stop matching any branch, and per ADR 0004 a poison message blocks its
group indefinitely. Wrapping makes the persisted name yours. This is documented, not enforced.

Concurrency stays per outbox. A cap shared across outboxes would let one module's slow handlers
starve another's workers with nothing in that module's configuration to explain it, which is the
coupling this whole change exists to remove. The arithmetic that follows —
`outboxes × 2 × MaxConcurrentGroups` worker loops against one connection pool, since the modules
share a connection string — is the operator's to do.

ADR 0001's stall costs more than it used to. An inbox handler holds a write transaction for its
whole duration and the stability gate of ADR 0002 is instance-wide, so a slow handler in one module
stalls head discovery for every other module in the process. The inbox keeps exactly-once rather
than gaining an at-least-once mode, because the trade would give up the property that makes an
inbox worth having to solve a problem an inbox handler should not have: its job is to write its own
module's tables and return.
