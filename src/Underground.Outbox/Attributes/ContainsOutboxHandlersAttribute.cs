namespace Underground.Outbox.Attributes;

/// <summary>
/// Marks an assembly as containing outbox/inbox message handlers, which the source generator scans for
/// cross-assembly handler discovery: <c>[assembly: ContainsOutboxHandlers]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ContainsOutboxHandlersAttribute : Attribute;
