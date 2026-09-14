# 01: Move the DI entry points out of the generator and into the library

**What to build:** `AddOutboxServices<TContext>` and `AddInboxServices<TContext>` stop being
generated and become ordinary source in `Underground.Outbox`.

`OutboxGenerator.GenerateDIMethod` is a constant string with no interpolated content, emitted
through `RegisterPostInitializationOutput` into the fixed namespace `Underground.Outbox.Configuration`.
Nothing about it varies per compilation, so generating it buys nothing — and once every project with
handlers runs the generator (03), a fixed-namespace type emitted into each of them is the CS0436
collision this design exists to avoid.

Landing this first means the rest of the work happens against entry points that already live where
they belong.

**Blocked by:** nothing.

**Status:** ready-for-agent

- [ ] `AddOutboxServices<TContext>` and `AddInboxServices<TContext>` exist as ordinary source in `Underground.Outbox`, in the namespace they are generated into today, with their current signatures and XML docs preserved.
- [ ] `GenerateDIMethod` and the `RegisterPostInitializationOutput` call are deleted from `OutboxGenerator`.
- [ ] The `OutboxDependencyInjection.g.cs` snapshots are deleted, and the generator test that asserts DI source is produced without handlers is retired or retargeted.
- [ ] The dispatcher registration inside those methods still runs; it is rewired in 04, not here.
- [ ] `dotnet build -warnaserror` is clean and the full suite passes.
