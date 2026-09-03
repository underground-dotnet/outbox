# A permanently failing message blocks its group, on purpose

A group offers only its Head for handling. A message that fails every attempt is retried with
exponential backoff up to a ten-minute ceiling, forever, and every message behind it in that group
waits. Other groups are unaffected.

This is not an oversight. Under strict per-group ordering the alternative — skipping past the
failing message so the group can proceed — silently drops a message out of an ordered stream,
which is worse than stalling for a consumer that depends on the order. Backoff alone does not
solve it either; it converts a hot retry loop into a silent stall, which is harder to notice.

Detection is therefore external: a group that stops draining shows up as unbounded growth in
unhandled messages, which is an alerting concern rather than a library one.

## Consequences

A dead-letter mechanism is a deliberate follow-up, expected to be configurable per inbox/outbox so
an application can choose stalling or dead-lettering. Until then, `Discard()` exception policies
remain the way to drop a known-bad message for a known exception type.

A crashed worker is not counted as a failed attempt, since no exception is ever observed. On the
outbox this self-limits: the lease is committed before dispatch, so a killed worker costs one
lease period. On the inbox the whole transaction rolls back and `RetryCount` stays where it was —
accepted, because an inbox handler that kills the process takes the application down with it,
which is a loud enough signal on its own.
