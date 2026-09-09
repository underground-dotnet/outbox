# The schema is chosen at registration, not by `search_path`

ADR 0005 fixed the table and column names and left the *location* of the tables to the connection:
the raw statements name `outbox` and `inbox` unqualified, and a deployment that must not collide
puts its `DbContext` in its own schema and says so on the connection's `search_path`. That answer
assumed one process holds one `DbContext`. It has no form in a modular monolith, where several
`DbContext`s each own a schema and share a single connection string: there is no per-outbox
`search_path` to set, so every worker resolves `outbox` to the same table and modules claim each
other's messages. Nothing errors — the table exists, the rows parse, and the wrong module handles
them.

So the schema becomes a required value at registration and the statements qualify the table:
`"orders".outbox`. Column names stay fixed. ADR 0005's argument against remapping them — that it
answers nothing but taste, and that a renamed column silently invalidates the partial index whose
filter is raw SQL — is untouched by this, because an index filter names columns and not schemas.

## Consequences

Statements are still literals, now composed per schema and cached against the schema string. The
machinery ADR 0005 removed does not come back: the cache key is a string written at registration,
not an `IModel`, so there is no `ConditionalWeakTable`, no identifier resolver, and no `S2743`
suppression.

`search_path` stops being a deployment requirement. A deployment that relied on it keeps working by
naming the same schema at registration.

The schema is required rather than read from the model. `AddOutboxServices<TContext>` runs against
an `IServiceCollection`, so no provider exists and `TContext.Model` cannot be reached; inferring
`HasDefaultSchema` would mean a lazy first-use resolution per outbox — more moving parts than one
string, for a value the consumer already writes once in `OnModelCreating`. Stating it twice is the
price of not building that.

Nothing validates it. A wrong schema fails at the first claim with `42P01`, which is loud enough.
The one failure that would be silent — two contexts registering the same schema, so two modules
share a table pair — is rejected at registration by an in-memory check on the schema strings. No
database is consulted at startup, deliberately: a startup query would fail every host that migrates
its own database after the host starts.
