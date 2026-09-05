# The message tables have fixed names, not names read off the EF model

The inbox lives in `inbox` and the outbox in `outbox`, with the column names spelled out in the
entity mappings and repeated verbatim in every raw statement. Neither can be remapped, and the raw
SQL names them unqualified, so both tables have to be reachable through the connection's
`search_path`.

Previously a consumer could remap the table, its schema and every column through ordinary EF Core
mappings, and the statements read the resulting identifiers back off `IModel`. That worked, but the
statements then depended on the model, so each one had to be built per model and cached against it:
two `ConditionalWeakTable<IModel, …>` keyed by model — one of them nested in a
`ConcurrentDictionary<Type, string>` because subclasses closed over the same entity shared the
field — both suppressing `S2743`, plus a class whose only job was to resolve and quote ten
identifiers. That is a large amount of machinery, and the two suppressed analyzer warnings say
plainly that it was fighting the language.

What it bought was uneven. Remapping a *table* answers a real operational need — an existing table
of that name, two applications sharing a schema, a house convention for infrastructure tables.
Remapping a *column* answers nothing but taste. And the flexibility had a sharp edge of its own: the
partial index that serves Head lookup is declared with a raw SQL filter, which an
`IEntityTypeConfiguration` cannot read the mapped name back out of, so a consumer who remapped
`ProcessedAt` silently got an index that no longer matched their column. Fixing the names removes
that failure mode entirely.

The operational need survives without remapping. A consumer who must not collide puts their
`DbContext`'s tables in their own schema; a fixed name inside a chosen schema costs them nothing.
That is why the raw SQL names the tables unqualified rather than hardcoding `public`: the schema is
the deployment's to choose, and `search_path` is how it says so.

## Consequences

Statements are literals. Only the guard shared by every write is a `const string`; the rest name their
table through `IMessage.TableName`, which is a `static abstract` property and so cannot appear in a
constant. A claim statement is composed once into a `static readonly` field on the class that owns it;
the two writes shared by both message types interpolate theirs per call, which is one string
concatenation against a database round trip and not worth a static field on a generic type — that is
what needed an `S2743` suppression before. Both per-model caches, both `S2743` suppressions and the
identifier resolver are gone, and `IDbContext.Model` went with them — it existed only to serve them.

A consumer who calls `ToTable` or `HasColumnName` on either entity gets no compile error and no
startup error. Their first claim fails against a table or column that is not there. This is
documented rather than validated: a startup check was considered and rejected as more machinery in
service of a case that documentation covers.

`search_path` becomes a deployment requirement. It is usually already satisfied — the tables live in
the default schema and the default `search_path` finds them — but an application that puts its
`DbContext` in a non-default schema has to say so on the connection.

The mapping annotations and the SQL literals are two places that must agree, with nothing but the
integration suite holding them together: a renamed column shows up as `42703 column … does not
exist` there, not in the fast test loop. That exposure is column names only — a table name is written
in the `[Table]` attribute and in `TableName` beside it, and every statement interpolates the latter
rather than spelling the table out a third time. `MessageColumns`, which shared the one name that used
to be written twice, is gone, since fixing every column name makes the drift it prevented a matter of
editing one literal and not the other.

A claim no longer reads its row column by column out of a `DbDataReader` on a hand-built `DbCommand`.
It runs through EF's `FromSqlRaw`, which materialises the entity from the result set by column name, so
the projection cannot drift from the entity the way a hand-written one could. This rode along with the
renaming rather than following from it, but it removes the second place a column name was spelled out
in C#, and the claim statements now have to return every mapped column. `AsNoTracking` on that call is
load-bearing: a tracked claim would let an application's `SaveChanges` inside a handler write the
message behind a `GuardedWrite`'s guard.

`ClaimHead` lost its `ILogger` and the debug log that recorded the statement it was about to run. The
statement is now a fixed literal per class rather than something assembled from a model, so logging it
told an operator nothing that reading the source does not.

`IMessage` gained a `static abstract string TableName`, so a generic write can name its table
without reflection or a lookup. It is public surface, and an external implementation of `IMessage`
would break — there is no supported path to being one, and the interface is only ever used as a
generic constraint.
