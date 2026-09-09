using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Underground.Outbox.SourceGenerator;

[Generator]
public sealed class OutboxGenerator : IIncrementalGenerator
{
    // when using CSharpCompilation vs CompilationProvider we lose some information from the type. Therefore we need to use < instead of <T>.
    private const string OutboxHandlerInterface = "Underground.Outbox.IOutboxMessageHandler<";
    private const string InboxHandlerInterface = "Underground.Outbox.IInboxMessageHandler<";
    private const string OutboxHandlerAttribute = "Underground.Outbox.Attributes.OutboxHandlerAttribute<";
    private const string InboxHandlerAttribute = "Underground.Outbox.Attributes.InboxHandlerAttribute<";
    private const string MarkerAttributeFullName = "Underground.Outbox.Attributes.ContainsOutboxHandlersAttribute";

    private static readonly DiagnosticDescriptor CompetingHandlers = new(
        id: "OUTBOX001",
        title: "Message type has competing handlers",
        messageFormat: "Message type '{0}' is handled by more than one {1} handler bound to '{2}' ({3}). Only one of them is dispatched.",
        category: "Underground.Outbox",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnboundHandler = new(
        id: "OUTBOX002",
        title: "Handler is not bound to a DbContext",
        messageFormat: "Handler '{0}' implements {1} but carries no [{2}<TContext>] attribute, so no dispatcher will ever call it",
        category: "Underground.Outbox",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Phase 1: Local handlers via SyntaxProvider
        // - Cached at syntax level (per-file changes)
        // - Uses record struct with value equality for proper cache comparison
        var localHandlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetsForGeneration(ctx))
            .SelectMany(static (handlers, _) => handlers)
            .Collect()
            // convert to EquatableList for proper cache comparison
            .Select(static (handlers, _) => ToEquatableList(handlers));

        // Phase 2: External handlers via MetadataReferencesProvider
        // - Only re-runs when referenced assemblies actually change (not on every keystroke)
        var externalHandlers = context.MetadataReferencesProvider
            .Select(static (reference, ct) => ScanReferencedAssembly(reference, ct))
            .SelectMany(static (handlers, _) => handlers)
            .Collect()
            .Select(static (handlers, _) => ToEquatableList(handlers));

        // Phase 3: Combine and generate
        // - EquatableList ensures proper cache comparison
        // - Output only regenerates when handler lists actually change
        var allHandlers = localHandlers.Combine(externalHandlers);

        context.RegisterSourceOutput(allHandlers, static (spc, source) =>
            {
                var (local, external) = source;
                Execute(local, external, spc);
            });
    }

    private static EquatableList<HandlerClassInfo> ToEquatableList(ImmutableArray<HandlerClassInfo> handlers)
    {
        var list = new EquatableList<HandlerClassInfo>();
        list.AddRange(handlers);
        return list;
    }

    // right now only supports direct implementations of the handler interfaces, but could be extended to support inherited classes as well if needed
    private static bool IsSyntaxTargetForGeneration(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDecl || classDecl.BaseList is null)
        {
            return false;
        }

        foreach (var baseType in classDecl.BaseList.Types)
        {
            // IOutboxMessageHandler<T> or IInboxMessageHandler<T>
            if (baseType.Type is GenericNameSyntax { Identifier.ValueText: "IOutboxMessageHandler" or "IInboxMessageHandler" })
            {
                return true;
            }

            // Namespace.IOutboxMessageHandler<T>
            if (baseType.Type is QualifiedNameSyntax { Right: GenericNameSyntax { Identifier.ValueText: "IOutboxMessageHandler" or "IInboxMessageHandler" } })
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<HandlerClassInfo> GetSemanticTargetsForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return [];
        }

        if (classSymbol.IsAbstract)
        {
            return [];
        }

        return GetHandlerInfos(classSymbol).ToImmutableArray();
    }

    /// <summary>
    /// Scans referenced assemblies marked with [ContainsOutboxHandlers] for handler types.
    /// Only called when MetadataReferencesProvider detects a change in references.
    /// </summary>
    private static EquatableList<HandlerClassInfo> ScanReferencedAssembly(MetadataReference reference, CancellationToken ct)
    {
        // Create a minimal compilation just to resolve symbols from metadata
        // This is cheaper than using the full project compilation
        var compilation = CSharpCompilation.Create(
            assemblyName: "temp",
            references: [reference]
        );

        if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
            return [];

        // Only scan assemblies marked with [ContainsOutboxHandlers]
        bool hasMarker = assembly.GetAttributes()
            .Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), MarkerAttributeFullName, StringComparison.Ordinal));

        if (!hasMarker)
            return [];

        var handlers = new EquatableList<HandlerClassInfo>();
        ScanNamespaceForHandlers(assembly.GlobalNamespace, handlers, ct);

        return handlers;
    }

    /// <summary>
    /// Recursively scans a namespace for handler implementations.
    /// </summary>
    private static void ScanNamespaceForHandlers(INamespaceSymbol ns, EquatableList<HandlerClassInfo> handlers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var type in ns.GetTypeMembers())
        {
            if (type.TypeKind == TypeKind.Class && !type.IsAbstract)
            {
                foreach (var info in GetHandlerInfos(type))
                {
                    handlers.Add(info);
                }
            }
        }

        foreach (var nestedNs in ns.GetNamespaceMembers())
        {
            ScanNamespaceForHandlers(nestedNs, handlers, ct);
        }
    }

    /// <summary>
    /// Extracts handler information from a type symbol if it implements a handler interface.
    /// </summary>
    private static IEnumerable<HandlerClassInfo> GetHandlerInfos(INamedTypeSymbol typeSymbol)
    {
        var outboxContext = BoundContext(typeSymbol, OutboxHandlerAttribute);
        var inboxContext = BoundContext(typeSymbol, InboxHandlerAttribute);

        foreach (var iface in typeSymbol.Interfaces)
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var originalDef = iface.OriginalDefinition.ToDisplayString();
            var message = iface.TypeArguments[0];

            if (originalDef.StartsWith(OutboxHandlerInterface, StringComparison.Ordinal))
            {
                yield return new HandlerClassInfo(
                    typeSymbol.ToDisplayString(),
                    Qualified(message),
                    message.ToDisplayString(),
                    outboxContext.Qualified,
                    outboxContext.Display,
                    HandlerKind.Outbox
                );
                continue;
            }

            if (originalDef.StartsWith(InboxHandlerInterface, StringComparison.Ordinal))
            {
                yield return new HandlerClassInfo(
                    typeSymbol.ToDisplayString(),
                    Qualified(message),
                    message.ToDisplayString(),
                    inboxContext.Qualified,
                    inboxContext.Display,
                    HandlerKind.Inbox
                );
            }
        }
    }

    /// <summary>
    /// The DbContext named in the handler's binding attribute, or empty strings when it carries none.
    /// </summary>
    private static (string Qualified, string Display) BoundContext(INamedTypeSymbol typeSymbol, string attributeName)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (attribute.AttributeClass is not { IsGenericType: true } attributeClass)
            {
                continue;
            }

            if (attributeClass.OriginalDefinition.ToDisplayString().StartsWith(attributeName, StringComparison.Ordinal))
            {
                var context = attributeClass.TypeArguments[0];
                return (Qualified(context), context.ToDisplayString());
            }
        }

        return (string.Empty, string.Empty);
    }

    /// <summary>
    /// A type as the generated code must write it: <c>global::</c>-qualified all the way down, including
    /// every type argument, so that no consumer namespace can shadow any part of it.
    /// </summary>
    private static string Qualified(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static void Execute(EquatableList<HandlerClassInfo> local, EquatableList<HandlerClassInfo> external, SourceProductionContext context)
    {
        // The same handler can be discovered twice when a reference is passed to the compiler more than
        // once; only genuinely competing handlers are an error.
        var localHandlers = local.Distinct().ToList();

        // Only local handlers are reported: an unattributed handler in a referenced assembly is that
        // assembly's own error, and reporting it here would blame every project that references it.
        ReportUnboundHandlers(localHandlers, context);

        var handlers = localHandlers
            .Concat(external)
            .Distinct()
            .Where(h => h.ContextQualifiedName.Length > 0)
            .ToList();

        ReportCompetingHandlers(handlers, context);

        var contexts = handlers
            .GroupBy(h => h.ContextQualifiedName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        context.AddSource("GeneratedDispatcher.g.cs", GenerateDispatchers(contexts));
        context.AddSource("OutboxDependencyInjection.g.cs", GenerateRegistrations(contexts));
    }

    /// <summary>
    /// One dispatcher per <c>DbContext</c>, so the set of message types a module's worker can dispatch is
    /// exactly the set that module declares. Internal, so two assemblies emitting for different contexts
    /// can never collide on a type name.
    /// </summary>
    private static string GenerateDispatchers(List<IGrouping<string, HandlerClassInfo>> contexts)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine();
        sb.AppendLine("using Underground.Outbox.Data;");
        sb.AppendLine("using Underground.Outbox.Domain.Dispatchers;");
        sb.AppendLine("using Underground.Outbox.Exceptions;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Underground.Outbox.Domain;");

        foreach (var group in contexts)
        {
            var contextType = group.Key;
            var contextName = group.First().ContextDisplayName;

            sb.AppendLine();
            sb.AppendLine($"/// <summary>Dispatches the messages of the {contextName} inbox and outbox to the handlers bound to it.</summary>");
            sb.AppendLine($"internal sealed class {DispatcherName(contextName)}<TMessage> : IMessageDispatcher<{contextType}, TMessage> where TMessage : class, IMessage");
            sb.AppendLine("{");
            sb.AppendLine("    public async Task ExecuteAsync(IServiceScope scope, TMessage message, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        var metadata = new MessageMetadata(message.EventId, message.GroupKey, message.RetryCount);");
            sb.AppendLine("        var serviceProvider = scope.ServiceProvider;");

            AppendDispatchBlock(sb, "InboxMessage", "IInboxMessageHandler", contextType, group.Where(h => h.Kind == HandlerKind.Inbox));
            sb.AppendLine();
            AppendDispatchBlock(sb, "OutboxMessage", "IOutboxMessageHandler", contextType, group.Where(h => h.Kind == HandlerKind.Outbox));
            sb.AppendLine();
            sb.AppendLine("        throw new ParsingException($\"Unsupported dispatcher message type {typeof(TMessage).FullName}\");");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        AppendFooter(sb);
        return sb.ToString();
    }

    /// <summary>
    /// One registration entry point per <c>DbContext</c>, named after it. Two assemblies emitting for the
    /// same context therefore collide as an ambiguous extension method rather than silently giving one
    /// module the other's dispatcher.
    /// </summary>
    private static string GenerateRegistrations(List<IGrouping<string, HandlerClassInfo>> contexts)
    {
        var sb = new StringBuilder();
        AppendHeader(sb);
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("using Underground.Outbox.Data;");
        sb.AppendLine("using Underground.Outbox.Domain;");
        sb.AppendLine("using Underground.Outbox.Domain.Dispatchers;");
        sb.AppendLine();
        sb.AppendLine("namespace Underground.Outbox.Configuration;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Registers the inbox and outbox of each DbContext this compilation declares handlers for.</summary>");
        sb.AppendLine("public static class ConfigureOutboxServices");
        sb.AppendLine("{");

        var methodNames = MethodNames(contexts);

        var emitted = 0;
        foreach (var group in contexts)
        {
            var contextName = group.First().ContextDisplayName;

            if (group.Any(h => h.Kind == HandlerKind.Outbox))
            {
                AppendRegistration(sb, group.Key, contextName, methodNames[group.Key], "Outbox", emitted++ > 0);
            }

            if (group.Any(h => h.Kind == HandlerKind.Inbox))
            {
                AppendRegistration(sb, group.Key, contextName, methodNames[group.Key], "Inbox", emitted++ > 0);
            }
        }

        sb.AppendLine("}");
        AppendFooter(sb);
        return sb.ToString();
    }

    private static void AppendRegistration(StringBuilder sb, string contextType, string contextName, string methodName, string side, bool precededByAnother)
    {
        var entity = side + "Message";

        if (precededByAnother)
        {
            sb.AppendLine();
        }

        sb.AppendLine($"    /// <summary>Registers the {contextName} {side.ToLowerInvariant()}. Set <c>Schema</c> to the schema its <c>{side.ToLowerInvariant()}</c> table lives in.</summary>");
        sb.AppendLine($"    public static IServiceCollection Add{methodName}{side}Services(");
        sb.AppendLine("        this IServiceCollection services,");
        sb.AppendLine($"        Action<{side}ServiceConfiguration<{contextType}>> configuration)");
        sb.AppendLine("    {");
        sb.AppendLine($"        services.AddScoped<IMessageDispatcher<{contextType}, {entity}>, {DispatcherName(contextName)}<{entity}>>();");
        sb.AppendLine($"        SetupServices.SetupInternal{side}Services<{contextType}>(services, configuration);");
        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// What each context's registration methods are named after: its own name, or - when two contexts in
    /// this compilation share that name - its full one. Two <c>AppDbContext</c>es in different namespaces
    /// would otherwise emit overloads that differ only in a lambda parameter, which every call site
    /// resolves as ambiguous.
    /// </summary>
    private static Dictionary<string, string> MethodNames(List<IGrouping<string, HandlerClassInfo>> contexts)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in contexts)
        {
            var contextName = group.First().ContextDisplayName;
            var simpleName = SimpleName(contextName);
            var shared = contexts.Count(other => string.Equals(SimpleName(other.First().ContextDisplayName), simpleName, StringComparison.Ordinal)) > 1;

            byName[group.Key] = shared ? Sanitize(contextName) : simpleName;
        }

        return byName;
    }

    private static void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
        sb.AppendLine();
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.AppendLine("#pragma warning restore CS1591");
    }

    /// <summary>
    /// Emits the dispatch chain for one message table of one context.
    /// </summary>
    /// <remarks>
    /// Each candidate is matched against <c>typeof(T).FullName</c> rather than a compile-time string
    /// literal, because that is the very expression the write side stores in the <c>type</c> column
    /// (see <c>OutboxMessage</c>). A literal taken from the compiler's own spelling of the type would
    /// disagree with it for nested types (<c>Outer.Inner</c> against <c>Outer+Inner</c>) and for generic
    /// ones, and the message would fall through to the unhandled arm at run time. That rules out a
    /// switch, whose labels have to be constants; the chain is linear in the number of handlers, which
    /// costs nothing beside the database round trip that delivered the message.
    ///
    /// The handler is resolved by the context type as key, so a neighbouring module handling the same
    /// message type cannot be given this module's message.
    /// </remarks>
    private static void AppendDispatchBlock(StringBuilder sb, string messageEntity, string handlerInterface, string contextType, IEnumerable<HandlerClassInfo> handlers)
    {
        sb.AppendLine($"        if (typeof(TMessage) == typeof({messageEntity}))");
        sb.AppendLine("        {");

        foreach (var classInfo in handlers.OrderBy(h => h.MessageTypeDisplayName, StringComparer.Ordinal))
        {
            var messageType = classInfo.MessageTypeQualifiedName;

            sb.AppendLine($"            if (string.Equals(message.Type, typeof({messageType}).FullName, StringComparison.Ordinal))");
            sb.AppendLine("            {");
            sb.AppendLine($"                var fullEvent = JsonSerializer.Deserialize<{messageType}>(message.Data) ?? throw new ParsingException($\"Cannot parse event body {{message.Data}} of message: {{message.Id}}\");");
            sb.AppendLine($"                var handler = serviceProvider.GetRequiredKeyedService<{handlerInterface}<{messageType}>>(typeof({contextType}));");
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            sb.AppendLine("                    await handler.HandleAsync(fullEvent, metadata, cancellationToken);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception ex) when (ex is not OperationCanceledException)");
            sb.AppendLine("                {");
            sb.AppendLine("                    throw new MessageHandlerException(");
            sb.AppendLine("                        handler.GetType(),");
            sb.AppendLine($"                        typeof({messageType}),");
            sb.AppendLine("                        $\"Error processing message {message.Id} with handler {handler.GetType().Name}\",");
            sb.AppendLine("                        ex");
            sb.AppendLine("                    );");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine();
        }

        sb.AppendLine("            throw new ParsingException($\"No handler configured for message type {message.Type} of message: {message.Id}\");");
        sb.AppendLine("        }");
    }

    /// <summary>
    /// Reports message types claimed by more than one handler bound to the same context. Two modules may
    /// handle one message type; one module may not, because only one of the two would ever run and which
    /// one is not something the author chose.
    /// </summary>
    private static void ReportCompetingHandlers(IEnumerable<HandlerClassInfo> handlers, SourceProductionContext context)
    {
        var competing = handlers
            .GroupBy(h => (h.Kind, h.ContextQualifiedName, h.MessageTypeDisplayName))
            .Where(g => g.Count() > 1);

        foreach (var group in competing)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompetingHandlers,
                Location.None,
                group.Key.MessageTypeDisplayName,
                group.Key.Kind == HandlerKind.Inbox ? "inbox" : "outbox",
                group.First().ContextDisplayName,
                string.Join(", ", group.Select(h => h.HandlerFullName).OrderBy(n => n, StringComparer.Ordinal))
            ));
        }
    }

    /// <summary>
    /// Reports handlers that name no <c>DbContext</c>. Nothing dispatches them, so shipping one is a
    /// silent no-op rather than a handler that runs against the wrong module's messages.
    /// </summary>
    private static void ReportUnboundHandlers(IEnumerable<HandlerClassInfo> handlers, SourceProductionContext context)
    {
        var unbound = handlers
            .Where(h => h.ContextQualifiedName.Length == 0)
            .GroupBy(h => (h.HandlerFullName, h.Kind))
            .OrderBy(g => g.Key.HandlerFullName, StringComparer.Ordinal);

        foreach (var group in unbound)
        {
            var inbox = group.Key.Kind == HandlerKind.Inbox;

            context.ReportDiagnostic(Diagnostic.Create(
                UnboundHandler,
                Location.None,
                group.Key.HandlerFullName,
                inbox ? "IInboxMessageHandler<T>" : "IOutboxMessageHandler<T>",
                inbox ? "InboxHandler" : "OutboxHandler"
            ));
        }
    }

    /// <summary>The dispatcher's type name, from the context's full name so two contexts never collide.</summary>
    private static string DispatcherName(string contextDisplayName) => "Dispatcher_" + Sanitize(contextDisplayName);

    /// <summary>The context's own name, which is what the registration methods are named after.</summary>
    private static string SimpleName(string contextDisplayName)
    {
        var lastDot = contextDisplayName.LastIndexOf('.');
        var name = lastDot < 0 ? contextDisplayName : contextDisplayName.Substring(lastDot + 1);
        return Sanitize(name);
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return sb.ToString();
    }
}
