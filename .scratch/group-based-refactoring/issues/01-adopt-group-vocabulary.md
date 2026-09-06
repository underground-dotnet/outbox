# 01: Adopt Group vocabulary

**What to build:** The library speaks the glossary's language. Everywhere the code called the
logical ordering-and-concurrency unit a "partition", it now calls it a Group. Nothing behaves
differently; this exists so that every later ticket is written in the right vocabulary, and so that
"partition" is free to mean PostgreSQL table partitioning and nothing else.

**Blocked by:** None (can start immediately).

**Status:** done

- [x] The message interface, both message types, and the handler metadata record expose `GroupKey` in place of `PartitionKey`, and the storage column is renamed to match.
- [x] The setting that caps how much work happens at once is named `MaxConcurrentGroups`.
- [x] The type that discovers available work is named for Groups rather than partitions.
- [x] Log messages, XML documentation and the example projects use "Group". No occurrence of "partition" remains anywhere except in reference to PostgreSQL table partitioning.
- [x] Source-generator output and its verified snapshots are regenerated.
- [x] The existing test suite passes with no behavioural change.
