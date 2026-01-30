namespace Underground.Outbox.Attributes;

/// <summary>
/// Marks an assembly as containing outbox/inbox message handlers.
/// The source generator will scan assemblies with this attribute to discover handlers.
/// </summary>
/// <remarks>
/// Add this attribute to your assembly to enable cross-assembly handler discovery:
/// <code>
/// [assembly: ContainsOutboxHandlers]
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ContainsOutboxHandlersAttribute : Attribute;
