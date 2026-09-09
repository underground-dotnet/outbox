# Context-bound inboxes and outboxes

Status: ready-for-agent

## Problem Statement

An application can have exactly one outbox and one inbox. Everything the library registers is keyed
on the message type alone — one `ConcurrentProcessor` per side, one configuration singleton per
side, one `IOutbox`, one `IInbox` — and the `DbContext` type is a generic parameter that is threaded
through registration and then never used. A modular monolith cannot use this. Each module owns its
own `DbContext` and its own PostgreSQL schema, and each module wants its own outbox and inbox, so
that a message a module writes is handled by that module's Handler against that module's tables.

Registering a second one today does not fail. It quietly misbehaves in four separate ways, none of
which reports an error:

- The second registration overwrites the `IDbContext` the whole library reads through, so Claims,
  retries, Completions and cleanup for *both* modules run against whichever `DbContext` registered
  last.
- The hosted service registration is de-duplicated by type, so the second module adds no worker at
  all. One worker drives one `ConcurrentProcessor` against one `DbContext`.
- Every Claim statement names its table unqualified, so the schema is decided by the connection's
  `search_path`. Modules that share a connection string share a `search_path`, so every module's
  worker resolves `outbox` to the same table and Claims another module's Outbox Messages.
- Handler registrations are added with `TryAddEnumerable`, which de-duplicates on implementation
  type, and the generated dispatcher resolves them with a plain `GetRequiredService`. When two
  modules each declare a Handler for the same message type, the last one registered wins and runs
  against the other module's messages.

Each of these produces a wrong result rather than an exception, and the wrongness is
cross-module — the failure appears in a module whose code did not change.

## Solution

An inbox and an outbox are bound to a `DbContext`. An application registers as many as it has
`DbContext`s, and everything that serves one — its configuration, its workers, its Handlers, its
statements — is reached through that `DbContext` type.

Three changes make that real.

**The `DbContext` becomes the key.** `IOutbox<TContext>` and `IInbox<TContext>` replace their
non-generic forms, and the internals that were generic only over the message type become generic
over the `DbContext` type as well. There is no longer a single `IDbContext` registration for the
library to read through.

**The schema is stated at registration.** Every Claim, Completion, retry and cleanup statement
qualifies its table with a schema given when the outbox is registered, so two modules on one
connection string reach two different table pairs. `search_path` stops being load-bearing. Table and
column names stay fixed, exactly as ADR 0005 left them. Registering two `DbContext`s against the
same schema is rejected in memory, at registration, because that is the one remaining way for two
modules to silently share a table pair.

**A Handler names its `DbContext`.** A Handler carries `[OutboxHandler<TContext>]` or
`[InboxHandler<TContext>]`. The source generator groups Handlers by that type and emits one
dispatcher per `DbContext`, and Handler registrations become keyed on the `DbContext` type, so
resolution can no longer cross a module boundary. Two modules may declare Handlers for the same
message type; one module may not.

What the library does **not** gain is any way to move a message between modules. It carries a
message from a module to itself. A module that wants to reach a neighbour calls it by whatever
in-process means the application already has, and the neighbour writes to its own inbox in its own
transaction. There is no transport here and adding several outboxes does not add one.

See ADR 0007 (schema at registration) and ADR 0008 (outboxes bound to a `DbContext`).

## User Stories

1. As an application developer, I want to register an outbox for each of my `DbContext`s, so that each module owns its own Outbox Messages.
2. As an application developer, I want to register an inbox for each of my `DbContext`s, so that each module owns its own Inbox Messages.
3. As an application developer, I want to register an outbox without an inbox, or an inbox without an outbox, so that a module that only produces or only consumes carries no table it does not use.
4. As an application developer, I want to resolve `IOutbox<TContext>` to write an Outbox Message, so that the compiler tells me which module's outbox I am writing to.
5. As an application developer, I want to resolve `IInbox<TContext>` to write an Inbox Message, so that the compiler tells me which module's inbox I am writing to.
6. As an application developer, I want an Outbox Message written through one module's outbox to be invisible to every other module's workers, so that a module's Group ordering is its own.
7. As an application developer, I want each outbox to Claim only from its own table, so that a slow or blocked module cannot consume another module's messages.
8. As an application developer, I want to state the schema when I register an outbox, so that my module's tables live beside the rest of my module's tables.
9. As an application developer, I want registration to fail immediately when two `DbContext`s are registered against the same schema, so that a copy-pasted module registration cannot silently make two modules share one table pair.
10. As an application developer, I want a wrong schema to fail loudly at the first Claim, so that I find out from a `42P01` rather than from messages that are never handled.
11. As an application developer, I want to keep running a single-outbox application without configuring a `search_path`, so that the schema I already declare on my `DbContext` is the only place I say it.
12. As a module owner, I want to declare a Handler that names my `DbContext`, so that my Handler can only ever be given my module's messages.
13. As a module owner, I want a Handler that names no `DbContext` to be a compile error, so that a Handler that would never be dispatched cannot be shipped.
14. As a module owner, I want two Handlers in my own module competing for one message type to be a compile error, so that a message with two plausible Handlers is caught before it is written.
15. As a module owner, I want my module and a neighbouring module to be able to declare Handlers for the same message type, so that a shared contract type does not force me to invent a wrapper.
16. As a module owner, I want my module's Handler to be resolved even when a neighbour declares a Handler for the same message type, so that a neighbour's registration cannot change what my messages do.
17. As a module owner, I want the dispatcher for my module to be emitted separately from every other module's, so that the set of message types my worker can dispatch is exactly the set my module declares.
18. As a module owner, I want to reference an assembly that declares Handlers for another `DbContext` without those Handlers joining my dispatcher, so that cross-assembly Handler discovery stays safe.
19. As a module owner, I want a Handler declared in another assembly and bound to my `DbContext` to be found, so that cross-assembly discovery keeps working as documented.
20. As a module owner, I want my Handler to keep the Handler interface it has today, so that adopting this costs an attribute rather than a rewrite.
21. As an application developer, I want a message whose stored type matches no branch of its module's dispatcher to fail as it does today, so that the poison-message behaviour I already understand is unchanged.
22. As an application developer, I want each outbox to have its own retry, backoff and `HandlerTimeout` settings, so that a module with a slow external partner does not impose its timeouts on a module without one.
23. As an application developer, I want each outbox to have its own exception policies, so that discarding one module's exception type does not discard another's.
24. As an application developer, I want each outbox to have its own `MaxConcurrentGroups`, so that a module with many Groups can be given more workers than a module with few.
25. As an application developer, I want no shared concurrency cap across outboxes, so that one module's slow Handlers cannot starve another module's workers.
26. As an operator, I want one hosted service to drive every registered outbox and inbox, so that starting and stopping the application starts and stops all of them together.
27. As an operator, I want one cleanup loop covering every registered outbox and inbox, so that retention does not cost one background loop per module.
28. As an application developer, I want each outbox to have its own `CompletedMessageRetention`, so that a module that needs a long inbox de-duplication window can have one without every module paying for it.
29. As an application developer, I want the save-changes interceptor to signal only the outbox belonging to the `DbContext` that committed, so that writing in one module does not wake every module's workers.
30. As an application developer, I want a commit in one module to wake that module's workers promptly, so that the low-latency path survives having several outboxes.
31. As an application developer, I want each module's Processing to use its own `DbContext`'s Execution Strategy, so that a module that configures retrying execution gets it and a module that does not is unaffected.
32. As an application developer, I want the `Lease` guard on Completion and retry to be evaluated against the correct module's table, so that at-least-once delivery holds per outbox.
33. As an application developer, I want each module's Middleware Pipeline to be composed independently, so that per-outbox configuration reaches the Middleware that reads it.
34. As an operator, I want traces and logs to identify which outbox a Processing Attempt belongs to, so that I can tell two modules' messages apart in one process.
35. As an application developer, I want the ordering guarantee within a Group to be unchanged, so that adopting this changes nothing about how my messages are sequenced.
36. As an application developer, I want Group keys to be understood as scoped to one inbox or outbox, so that I do not assume two modules using the same Group key are serialised against each other.
37. As a library maintainer, I want this to ship as one breaking major version, so that consumers absorb the generic parameters, the Handler attribute and the required schema in a single upgrade.
38. As a consumer upgrading, I want the breaking changes documented together with what each one replaces, so that I can mechanically translate an existing single-outbox registration.
39. As an application developer, I want to know that wrapping a neighbour's contract type is optional, so that I can choose the ceremony rather than have it imposed.
40. As an application developer, I want to know that an unwrapped message persists a neighbour's type name, so that I can judge whether their rename poisoning my un-Completed rows is a risk I accept.
41. As an application developer, I want the instance-wide stall caused by an inbox Handler holding a write transaction to be documented, so that I know a slow Handler in one module delays Head Message discovery in every other.
42. As an operator, I want the worker-to-connection arithmetic documented, so that I can size a pool that several modules share through one connection string.

## Implementation Decisions

**`DbContext` as the key throughout.** `IOutbox` and `IInbox` become `IOutbox<TContext>` and
`IInbox<TContext>`. The registration entry points keep their existing `TContext` generic parameter
and start using it: the per-side configuration object, the `ConcurrentProcessor`, the processor, the
Claim statement holder, the Middleware Pipeline, the retention deleter and the message-writing
services all become generic over `(TContext, TEntity)` rather than `TEntity` alone. The scoped
`IDbContext` / `IOutboxDbContext` / `IInboxDbContext` registrations that the whole library currently
reads through are removed; each context-bound service resolves its own `TContext` directly. This is
what retires the `S2743` suppression on the shared registration helper, whose unused `TContext`
parameter is the vestige of this design.

**Schema at registration.** The per-side configuration gains a required `Schema`. Registration
throws when it is unset. Every raw statement qualifies its table with it. Statements remain literals,
composed per schema string and cached against that string — not against `IModel`, so none of the
per-model machinery ADR 0005 removed returns. The schema is *not* inferred from `HasDefaultSchema`:
registration runs against an `IServiceCollection`, so the model cannot be reached without a lazy
first-use resolution, which is more moving parts than one string.

**Duplicate-schema rejection.** Registration keeps the set of schemas already claimed and throws
when a second `DbContext` claims one. This is an in-memory check with no database access. It is the
only startup validation: there is no `information_schema` probe and no model-level assertion, because
a startup query fails any host that migrates its own database after starting.

**Handler-to-context binding by attribute.** New attributes `[OutboxHandler<TContext>]` and
`[InboxHandler<TContext>]`, generic over a type constrained to `DbContext`. `IOutboxMessageHandler<T>`
and `IInboxMessageHandler<T>` are unchanged, so an existing Handler adopts this by gaining an
attribute. The attribute is required: a Handler implementing either interface without one is a new
error diagnostic (`OUTBOX002`).

**Generator output shape.** The generator groups discovered Handlers by the `DbContext` named in
their attribute and emits one dispatcher per `DbContext`. Dispatchers are `internal`, so two
assemblies emitting for different contexts can never collide on a type name. Each context gets a
public registration entry point named after it, so two assemblies emitting for the *same* context
collide as an ambiguous extension method — wrong, but loud. Cross-assembly discovery through the
existing assembly-level marker attribute is retained: grouping is explicit, so a referenced
assembly's Handlers land under their own context rather than joining the referencing assembly's
dispatcher.

**Diagnostics.** The existing competing-handler diagnostic becomes per `DbContext`: two Handlers for
one message type bound to the same context is still an error; the same message type handled by two
Handlers bound to different contexts is legal. The new unattributed-handler diagnostic is an error.

**Keyed Handler resolution.** Handler service descriptors are registered keyed on the `DbContext`
type, and the generated dispatcher resolves them with the keyed lookup. This is the change that
makes wrapping optional: with unkeyed `TryAddEnumerable` registrations, two modules handling one
message type resolve to whichever registered last.

**Worker topology.** One hosted service enumerates the registered processors through a non-generic
seam resolved as a collection, and starts one worker set per registered outbox and inbox. One
cleanup loop covers all of them. `MaxConcurrentGroups` stays per outbox and there is no cap shared
across them.

**Interceptor.** The save-changes interceptor becomes context-bound so that a commit signals only the
outbox and inbox belonging to the `DbContext` that committed.

**Unchanged.** Table and column names stay fixed (ADR 0005). The inbox keeps its single-transaction
exactly-once model and the outbox its three-transaction at-least-once model (ADR 0001). Ordering,
Stability, the `(TransactionId, Id)` order and Head Message discovery (ADR 0002) are untouched. No
batching (ADR 0003). Poison messages still block their Group (ADR 0004). No transport and no relay
between modules.

## Testing Decisions

A good test here asserts on what a consumer can observe: which Handler ran, which rows a module's
worker Claimed and Completed, what the generator emitted for a given source, and whether
registration threw. It does not assert on which services are registered, on the generic arity of
internal types, or on the composition of the Middleware Pipeline.

**Generator behaviour** is tested at the existing seam that runs the generator over a source string
and snapshots the generated files and diagnostics, with the existing snapshot tests as prior art.
Cases: one context with Handlers on both sides; two contexts each with a Handler for the same message
type; two Handlers on one context competing for one message type (error); a Handler with no attribute
(error); an abstract Handler (ignored, as today); a Handler discovered through the assembly-level
marker attribute in a referenced assembly and bound to a different context. Two changes to the
existing helper are required: the test compilation must reference EF Core, because the attribute's
type parameter is constrained to `DbContext`; and the existing exclusion of the dependency-injection
output from snapshots must go, since that file stops being identical for every run once its entry
point is named per context.

**Runtime behaviour** is tested at the existing seam that drives the concurrent processor to idle
against a per-test database, with the existing outbox integration tests as prior art. Cases: two
outboxes in one provider, each Claiming only its own rows; a message type handled by both contexts
resolving to the correct Handler for each; a schema-qualified Claim reaching the right table; a
commit through one module's `DbContext` waking only that module's workers; one hosted service
starting workers for every registered outbox, using the existing start/stop of the hosted-service
collection as prior art. This needs fixture work rather than a new seam: the test contexts declare a
default schema and the template database is created with more than one schema.

**Registration-time behaviour** is tested at the existing configuration test seam, which asserts on
configuration without a database. Cases: a missing schema throws; two contexts on one schema throw;
two contexts on different schemas succeed.

## Out of Scope

Any transport, relay, or delivery of a message from one module to another. The library carries a
message from a module to itself; reaching a neighbour is the application's own in-process call, and
the neighbour's write to its own inbox is an ordinary inbox write.

Any startup validation that touches the database — no `information_schema` probe, no check that the
tables exist, no assertion that the entity's mapped schema matches the configured one.

Any enforcement of message wrapping. Wrapping a neighbour's contract type in a module-local type is
documented as a durability choice and is not checked.

Any change to the inbox transaction model. An at-least-once inbox mode was considered and rejected;
the instance-wide stall from a long-running inbox Handler is accepted and documented.

Any shared concurrency or connection budget across outboxes.

Any compatibility shim for the non-generic `IOutbox` / `IInbox`, and any renumbering of the two ADRs
that currently share the number 0006.

Changes to the modulith template that consumes this. Placement of the registration calls across a
module's layers, and where a wrapper type lives relative to a module's contract projects, are the
consumer's concern.

## Further Notes

The vocabulary changes with this work: an inbox or an outbox becomes countable, and `Group` stops
being "the only source of parallelism" without qualification, because Group keys are not comparable
across two modules' tables. `CONTEXT.md` has been amended accordingly.

The four silent failures listed in the Problem Statement are worth keeping in view while
implementing, because each is a test: a second registration must not overwrite a shared service, must
add its own worker, must Claim from its own schema, and must resolve its own Handler.
