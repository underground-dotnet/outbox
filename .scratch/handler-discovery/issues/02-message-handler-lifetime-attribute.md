# 02: The `[MessageHandlerLifetime]` attribute

**What to build:** A new optional attribute stating the DI lifetime of a handler, and the deletion
of the marker attribute it replaces.

The name is deliberately neutral between the two sides. `CONTEXT.md` treats **Outbox Message** and
**Inbox Message** as opposite-direction concepts rather than one umbrella, and this attribute lands
on implementations of both interfaces, which share the `MessageHandler` stem.

Nothing reads the attribute yet — 03 does. This ticket only introduces the type and removes the one
it supersedes, so that the generator change is a single coherent diff.

**Blocked by:** nothing.

**Status:** done

- [x] `MessageHandlerLifetimeAttribute` exists in `Underground.Outbox.Attributes`, is `sealed`, targets classes only, takes a `Microsoft.Extensions.DependencyInjection.ServiceLifetime`, and exposes it.
- [x] Its XML `<summary>` says it is optional and that a handler without it is Transient; a `<remarks>` records that it governs both inbox and outbox handlers.
- [x] `ContainsOutboxHandlersAttribute` is deleted, along with `example/MultiProjectLib/AssemblyInfo.cs`.
- [x] `dotnet build -warnaserror` is clean.
