# A message carries its writer's trace context, and the process span is its child

Both message tables gain a nullable `traceparent` column. `IOutbox.AddMessageAsync` and its siblings
stamp it from the ambient `Activity` at the moment the message is added, and `TraceMessageMiddleware`
— the outermost middleware — starts the OpenTelemetry *process* span as a **child** of that context.

The column exists because nothing else can carry the link. Npgsql's instrumentation already traces
every statement this library runs, but a worker handles a message minutes later, on a background
thread, in a different process, with no ambient `Activity` — so today every one of those database
spans is a root span in a trace of its own. Storing the writer's context is the only way the
transaction that caused the message and the Handler that carries out its effect can appear in one
trace.

Parenting rather than linking is the part worth explaining, because the semantic conventions make
links the default. Links are the default to accommodate batches and to leave room for an ambient
context that the process span must belong to instead. Neither applies here: a worker claims exactly
one Head Message (ADR 0003), and there is no ambient context to compete with. The conventions
explicitly permit the creation context as the parent for single-message scenarios, and that is the
shape that produces the end-to-end waterfall the column was added for.

## Consequences

The sampling decision is inherited from the writer. An application sampling at one percent keeps the
process span for one message in a hundred, failures included. The failures are still logged — the
outcome line in `LogMessageMiddleware` does not depend on sampling — but they are not all traced. A
consumer who wants every message handled traced configures a sampler that says so.

Trace duration is unbounded. A message scheduled through `VisibleAt` for next week joins a trace
opened last week, and a message failing repeatedly under ADR 0004 adds a process span to that trace
on every attempt. Most tracing backends assume traces close in seconds. This is accepted: the
scheduled case is a minority, and a trace showing forty attempts against one message is a good
artifact rather than a defect.

The context is captured from `Activity.Current` at add time and nowhere else. An inbox transport
adapter that reads a `traceparent` off a broker header without starting an `Activity` has no way to
get it into the row, so the trace breaks at the ingestion boundary. The remedy, if it is ever needed,
is an optional constructor parameter — an additive change that costs no migration.

Adding messages by tracking them directly (`context.OutboxMessages.Add`) still works and is still
processed; those messages simply have no context and are handled under a trace of their own. This is
documented rather than prevented.

`traceparent` is a column name, so ADR 0005 makes it a contract with deployments: consumers pick it
up as an EF migration. It is nullable and additive, so existing rows and non-tracing producers need
nothing. `tracestate` is deliberately not stored — it is meaningful only when a vendor upstream sets
it and a vendor downstream reads it, which a message crossing this library's own tables does not do.

An unparseable value in the column starts a new trace instead of throwing. The outermost middleware
throwing would stall the Group under ADR 0004, which is far too high a price for a malformed
observability field.

No metrics. A lost Lease is now visible on the span as `error.type`, distinguishing `lease_lost`
(the Handler threw and the attempt was never recorded) from `duplicate_delivery` (the Handler
succeeded and the completion write came too late, so the effect will happen again). Counters and a
head-message-age gauge remain separate work.
