# 04: Retry backoff via a visibility instant

**What to build:** A handler that fails no longer retries immediately and forever. Every message
carries the instant from which it may be handled, and a failed attempt pushes that instant into the
future by an exponentially increasing, jittered, capped delay. A temporarily unavailable partner
system stops being hammered.

**Blocked by:** 03 (ordering that survives concurrent producers). Sequencing rather than a
functional dependency: the two columns are orthogonal, but both touch the same entity and fetch
query.

**Status:** done

- [x] Every message carries the instant from which it may be handled, defaulted by the database to the present.
- [x] A message whose instant lies in the future is not handled.
- [x] A failed attempt sets that instant to an exponentially increasing delay based on the attempt count, with jitter, capped at a configured maximum.
- [x] Base delay, maximum delay and jitter proportion are configurable.
- [x] Delays are expressed as intervals and anchored by the database clock. The application never supplies an absolute instant, so a skewed application clock cannot affect timing.
- [x] Tests simulate elapsed time by moving the stored instant into the past rather than by waiting.
- [x] A test proves successive failures produce increasing delays and that growth stops at the cap.

## Comments

`VisibleAt` is `timestamptz` defaulted with `clock_timestamp()`, configured next to `TransactionId`
in `MessageConfiguration<TEntity>` as `HasDefaultValueSql` plus `ValueGeneratedOnAdd`. EF then omits
the column from the insert while the property still holds its CLR default, which is both how the
database gets to assign "the present" and the room 05 needs for a caller-supplied instant.

The failure path writes `visible_at = clock_timestamp() + @delay` as raw SQL (`ScheduleRetry`)
rather than through `ExecuteUpdateAsync`, and this is the load-bearing detail of the ticket. Npgsql
translates `DateTime.UtcNow` to `now()`, which is frozen for the transaction - and the inbox holds
one open across its handler, so every retry in one transaction would be scheduled from the same
instant. Passing an instant computed in .NET instead would put delivery timing on each instance's
clock. Only an interval crosses the wire.

That second raw-SQL statement is why table and column resolution moved out of `FetchMessages` into
`MessageTable`. A consumer may remap the table, its schema and every column, so neither statement
can name them literally, and two independent copies of that resolution would be two ways to get it
wrong.

Jitter is applied after the cap, so a delay can exceed `MaxBackoff` by the jitter proportion.
Clamping it back to the cap would pile every long-backed-off Group onto the same ceiling instant,
which is what jitter exists to prevent. The doubling is computed in `double` rather than in ticks:
a message that fails forever passes the point where doubling overflows a `TimeSpan`, and `Math.Pow`
saturating at infinity is resolved by the cap.

`BackoffJitter` is validated as `[0, 1)`. At 1 or above the spread could produce a zero or negative
delay, which retries immediately - exactly the behaviour the ticket removes.

One thing lands deliberately wrong here and is 06's to fix. The visibility test is a plain
`visible_at <= clock_timestamp()` in the fetch's `WHERE`, which means that while a Group's Head is
in backoff the query hands out the messages *behind* it. Before this change a failing Head stalled
its Group by stopping the batch; now it stops blocking. That is the reordering 06 exists to
prevent, and the spec sequences it this way on purpose - the correct form needs the two-stage Head
discovery that 06 introduces, so writing an interim version of it here would only be thrown away.

Time is simulated by moving `visible_at` into the past, never by waiting, so `VisibleAtTests` costs
no more than any other database test. The delay-growth test asserts ranges rather than equalities
because the delay is measured against the database clock some milliseconds into the interval it
granted; the exact law is covered by `RetryBackoffTests`, which drives the computation directly with
jitter switched off. One test does really wait, for a 250ms backoff, because moving the column
proves the rule but not that a delay left alone ever expires. It only asserts the positive - that
after waiting the message comes back - so a slow run cannot turn it into a race.

Two gaps are accepted rather than closed. The backoff is generic over both entities but only
exercised on the outbox, because the test project has no inbox fixture at all - no `IInboxDbContext`
test context, no inbox handler - and building one is its own piece of work rather than a corner of
this ticket. And nothing tests the clock-anchoring criterion directly: a test would have to skew the
application's clock away from the container's. It holds by construction instead - only a `TimeSpan`
is ever sent as a parameter, and every instant in the statement comes from `clock_timestamp()`.
