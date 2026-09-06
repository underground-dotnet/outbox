# 05: Scheduled delivery

**What to build:** A caller can say "do not handle this before nine tomorrow morning" when creating
a message, instead of building a scheduler alongside the library.

**Blocked by:** 04 (retry backoff via a visibility instant). The header originally said this runs in
parallel with 06 onwards; it does not entirely - see the Comments.

**Status:** done

- [x] Message creation accepts an optional earliest-handling instant.
- [x] Omitting it means "as soon as possible"; existing callers are unaffected.
- [x] The parameter's documentation states that scheduling a message also delays every later message in its Group, because a Group offers only its Head.
- [x] A test proves a scheduled message is not handled before its instant and is handled after it.
- [x] A test proves a scheduled message holds back later messages in the same Group while leaving other Groups unaffected. **Delivered by 06** as `MessagesBehindAScheduledHeadAreNotHandledUntilThatHeadHasBeen` - see Comments.

## Comments

`visibleAt` is a trailing optional `DateTime?` on both public constructors of both entities, so no
existing call site changes. `null` becomes `default(DateTime)`, which is the sentinel EF uses to
decide that a `ValueGeneratedOnAdd` property was never set: the column is then left out of the insert
and 04's `clock_timestamp()` default assigns the present. Setting the property is therefore the whole
implementation - the room for it was already made in 04. The corollary is that `DateTime.MinValue`
cannot be told apart from "unscheduled", which costs nothing, since scheduling a message for the
beginning of time and not scheduling it at all mean the same thing.

The instant is absolute and comes from the application, which looks like it contradicts the spec's
rule that only intervals cross the wire. It does not: that rule exists so that *library* timing -
backoff and lease expiry - cannot depend on a skewed instance clock. A scheduled instant is the
caller's intent, not the library's timing, and "nine tomorrow morning" cannot be expressed as an
interval anyway. `ScheduledInstantIsStoredAsSuppliedRatherThanDefaultedToThePresent` measures the
stored value against `clock_timestamp()`, so a mistranslation between the two frames fails the test.

Kind is left to Npgsql rather than coerced. It rejects `Unspecified` against `timestamptz` and
converts `Local`, which is the same contract `CreatedAt` has always had; documenting the parameter as
UTC and letting the driver's error stand beats silently assuming a zone for someone who passed a
local wall-clock time.

### The deferred criterion

The last criterion is 06's to close, and the ticket is wrong to place it here. Its two halves are not
equally available today. "Other Groups unaffected" holds already and is tested
(`ScheduledMessageDoesNotDelayOtherGroups`). "Holds back later messages in the same Group" does not:
`FetchMessages` still filters `visible_at <= clock_timestamp()` in the `WHERE`, so a Group whose Head
is invisible hands out the message *behind* it. 04's comments record this deliberately, and 06 is the
ticket that replaces the filter with two-stage Head discovery. Writing an interim fix here would be
thrown away by 06, and the spec's own delivery sequence agrees - it lists scheduled delivery as step
6, after Head discovery, not in parallel with it.

06 already carries the equivalent criterion for a Head in backoff. The scheduled variant has been
added to it there; the two differ only in how the Head became invisible. Note also that
`ScheduledMessageDoesNotDelayOtherGroups` passes today partly because of the very `WHERE` filter 06
removes, so it has to be re-proved once Head discovery lands.

### The untested side

`visibleAt` is on `InboxMessage` as well, and nothing exercises it there. This is 04's gap inherited
rather than a new one: the test project still has no inbox fixture at all - no `IInboxDbContext` test
context and no inbox handler - so an inbox test would mean building that first. The two entities carry
the same column, the same configuration and the same one-line assignment, so the risk is a divergence
between two files rather than a behaviour that was never thought through.
