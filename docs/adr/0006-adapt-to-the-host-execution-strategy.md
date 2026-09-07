# Adapt to the host's execution strategy rather than forbidding it

The library resolves `IDbContext` from the application's own `DbContext`, so the host's EF
configuration governs the library's internals as well as the caller's code. A host that configures a
retrying execution strategy — which Aspire's `EnrichNpgsqlDbContext` does by default — makes EF
refuse every strategy-covered operation inside a transaction the caller began itself, and both
processors begin one and then query inside it. The alternative considered
was declaring a non-retrying context a precondition and failing at startup, in the manner of ADR
0005. It was rejected because the outbox context is the application's main context: the precondition
would cost the host its retries everywhere to solve a problem local to two methods.

Both processors therefore run their transaction through
`Database.CreateExecutionStrategy().ExecuteAsync(...)`, which is a no-op when no retry is
configured, so there is one code path rather than a branch. The retry unit differs per side, exactly
as the transaction boundary does in ADR 0001. The outbox wraps **only its claim**: its dispatch and
its outcome write open no transaction of their own, so each is covered one statement at a time, and
wrapping the whole attempt would replay the Handler for a transient failure in the write that
follows it. The inbox has one transaction spanning claim, Handler and outcome, so
its retry unit is the whole attempt.

For the caller, `IDbContext.ExecuteInTransactionAsync` owns the transaction so that adding a message
reads the same with or without a retrying strategy. It joins an already-open transaction rather than
refusing, so a method using it stays callable from inside another one.

## Consequences

An inbox Handler may be **run** more than once; what remains exactly-once is its effect on this
database. Either the replayed attempt rolled back entirely, or it committed and the replayed claim
finds the message no longer offered. This makes "effects confined to this database's transaction" a
constraint on inbox Handlers rather than a convention, and ADR 0001's exactly-once claim is now a
claim about the effect, not about the invocation.

A replayed inbox attempt reuses its scope and its `DbContext`, so it clears the change tracker
before it begins; the failed attempt's tracked entities belong to a transaction that no longer
exists. Every middleware is stateless, so the change tracker is the only per-attempt state a replay
inherits. A per-attempt service scope would have removed that reasoning step, at the cost of making
`IProcessor<TEntity>` own its own scopes; it was judged not worth it while the middleware stay
stateless.

An outbox claim whose commit fails ambiguously grants a Lease that no worker holds: the replayed
claim finds the message invisible and reports nothing offered. The message is delivered a lease
duration later than it would otherwise have been, which the design already tolerates.

The outcome writes — `MarkCompleted` and `ScheduleRetry` — go through `ExecuteSqlRawAsync`, which
does not apply the execution strategy, unlike a LINQ query or `SaveChanges`. They are deliberately
left that way: this decision is about working under a host that configures retries, not about adding
retries of its own. A transient failure in an outbox outcome write therefore behaves exactly as it
did before — the attempt fails, the Lease expires, and the message is offered again — which is the
redelivery the outbox is already built to absorb. On the inbox those writes sit inside the retried
attempt, so a failure there replays the attempt.

Callers pass a re-runnable delegate. Work staged on the context *before* the call is not replayed
with it, so a delegate that depends on it can see a context that a rolled-back attempt left dirty.
