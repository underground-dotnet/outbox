# 05: Scheduled delivery

**What to build:** A caller can say "do not handle this before nine tomorrow morning" when creating
a message, instead of building a scheduler alongside the library.

**Blocked by:** 04 (retry backoff via a visibility instant). Runs in parallel with 06 onwards.

**Status:** ready-for-agent

- [ ] Message creation accepts an optional earliest-handling instant.
- [ ] Omitting it means "as soon as possible"; existing callers are unaffected.
- [ ] The parameter's documentation states that scheduling a message also delays every later message in its Group, because a Group offers only its Head.
- [ ] A test proves a scheduled message is not handled before its instant and is handled after it.
- [ ] A test proves a scheduled message holds back later messages in the same Group while leaving other Groups unaffected.
