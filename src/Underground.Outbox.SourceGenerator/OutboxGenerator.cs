using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Underground.Outbox.SourceGenerator;

[Generator]
public sealed class OutboxGenerator : IIncrementalGenerator
{
    // when using CSharpCompilation vs CompilationProvider we lose some information from the type. Therefore we need to use < instead of <T>.
    private const string OutboxHandlerInterface = "Underground.Outbox.IOutboxMessageHandler<";
    private const string InboxHandlerInterface = "Underground.Outbox.IInboxMessageHandler<";
    private const string MarkerAttributeFullName = "Underground.Outbox.Attributes.ContainsOutboxHandlersAttribute";

    private static readonly DiagnosticDescriptor CompetingHandlers = new(
        id: "OUTBOX001",
        title: "Message type has competing handlers",
        messageFormat: "Message type '{0}' is handled by more than one {1} handler ({2}). Only one of them is dispatched.",
        category: "Underground.Outbox",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx => ctx.AddSource(
            "OutboxDependencyInjection.g.cs",
            SourceText.From(GenerateDIMethod, Encoding.UTF8))
        );

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
                var combined = new EquatableList<HandlerClassInfo>();
                combined.AddRange(local);
                combined.AddRange(external);
                Execute(combined, spc);
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
        // return node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 };

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
        foreach (var iface in typeSymbol.Interfaces)
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var originalDef = iface.OriginalDefinition.ToDisplayString();

            if (originalDef.StartsWith(OutboxHandlerInterface, StringComparison.Ordinal))
            {
                yield return new HandlerClassInfo(
                    typeSymbol.ToDisplayString(),
                    iface.TypeArguments[0].ToDisplayString(),
                    HandlerKind.Outbox
                );
                continue;
            }

            if (originalDef.StartsWith(InboxHandlerInterface, StringComparison.Ordinal))
            {
                yield return new HandlerClassInfo(
                    typeSymbol.ToDisplayString(),
                    iface.TypeArguments[0].ToDisplayString(),
                    HandlerKind.Inbox
                );
            }
        }
    }

    private static void Execute(EquatableList<HandlerClassInfo> handlers, SourceProductionContext context)
    {
        // The same handler can be discovered twice when a reference is passed to the compiler more than
        // once; only genuinely competing handlers are an error.
        var distinctHandlers = handlers.Distinct().ToList();
        ReportCompetingHandlers(distinctHandlers, context);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine();
        sb.AppendLine("using Underground.Outbox.Data;");
        sb.AppendLine("using Underground.Outbox.Domain.Dispatchers;");
        sb.AppendLine("using Underground.Outbox.Exceptions;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Underground.Outbox.Domain;");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
        sb.AppendLine("public class GeneratedDispatcher<TMessage> : IMessageDispatcher<TMessage> where TMessage : class, IMessage");
        sb.AppendLine("{");
        sb.AppendLine("    public async Task ExecuteAsync(IServiceScope scope, TMessage message, CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        if (distinctHandlers.Count != 0)
        {
            sb.AppendLine("        var metadata = new MessageMetadata(message.EventId, message.GroupKey, message.RetryCount);");
            sb.AppendLine("        var serviceProvider = scope.ServiceProvider;");
        }

        AppendDispatchBlock(sb, "InboxMessage", "IInboxMessageHandler", distinctHandlers.Where(h => h.Kind == HandlerKind.Inbox));
        sb.AppendLine();
        AppendDispatchBlock(sb, "OutboxMessage", "IOutboxMessageHandler", distinctHandlers.Where(h => h.Kind == HandlerKind.Outbox));
        sb.AppendLine();
        sb.AppendLine("        throw new ParsingException($\"Unsupported dispatcher message type {typeof(TMessage).FullName}\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("#pragma warning restore CS1591");

        context.AddSource("GeneratedDispatcher.g.cs", sb.ToString());
    }

    /// <summary>
    /// Emits the dispatch chain for one message table.
    /// </summary>
    /// <remarks>
    /// Each candidate is matched against <c>typeof(T).FullName</c> rather than a compile-time string
    /// literal, because that is the very expression the write side stores in the <c>type</c> column
    /// (see <c>OutboxMessage</c>). A literal taken from the compiler's own spelling of the type would
    /// disagree with it for nested types (<c>Outer.Inner</c> against <c>Outer+Inner</c>) and for generic
    /// ones, and the message would fall through to the unhandled arm at run time. That rules out a
    /// switch, whose labels have to be constants; the chain is linear in the number of handlers, which
    /// costs nothing beside the database round trip that delivered the message.
    /// </remarks>
    private static void AppendDispatchBlock(StringBuilder sb, string messageEntity, string handlerInterface, IEnumerable<HandlerClassInfo> handlers)
    {
        sb.AppendLine($"        if (typeof(TMessage) == typeof({messageEntity}))");
        sb.AppendLine("        {");

        foreach (var classInfo in handlers)
        {
            var messageType = classInfo.MessageTypeDisplayName;

            sb.AppendLine($"            if (string.Equals(message.Type, typeof({messageType}).FullName, StringComparison.Ordinal))");
            sb.AppendLine("            {");
            sb.AppendLine($"                var fullEvent = JsonSerializer.Deserialize<{messageType}>(message.Data) ?? throw new ParsingException($\"Cannot parse event body {{message.Data}} of message: {{message.Id}}\");");
            sb.AppendLine($"                var handler = serviceProvider.GetRequiredService<{handlerInterface}<{messageType}>>();");
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
    /// Reports message types claimed by more than one handler of the same kind. Only one of them would
    /// ever run, and which one is not something the author chose.
    /// </summary>
    private static void ReportCompetingHandlers(IEnumerable<HandlerClassInfo> handlers, SourceProductionContext context)
    {
        var competing = handlers
            .GroupBy(h => (h.Kind, h.MessageTypeDisplayName))
            .Where(g => g.Count() > 1);

        foreach (var group in competing)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CompetingHandlers,
                Location.None,
                group.Key.MessageTypeDisplayName,
                group.Key.Kind == HandlerKind.Inbox ? "inbox" : "outbox",
                string.Join(", ", group.Select(h => h.HandlerFullName).OrderBy(n => n, StringComparer.Ordinal))
            ));
        }
    }

    private static readonly string GenerateDIMethod = @"// <auto-generated/>
#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Data;
using Underground.Outbox.Domain;
using Underground.Outbox.Domain.Dispatchers;

namespace Underground.Outbox.Configuration;

/// <summary>
/// Provides extension methods for configuring and setting up Outbox and Inbox services in the dependency injection container.
/// </summary>
public static class ConfigureOutboxServices
{
    /// <summary>
    /// Configures and registers Outbox services in the dependency injection container.
    /// </summary>
    /// <typeparam name=""TContext"">The DbContext type that implements IOutboxDbContext.</typeparam>
    /// <param name=""services"">The service collection to register services with.</param>
    /// <param name=""configuration"">An action to configure the Outbox service settings.</param>
    public static IServiceCollection AddOutboxServices<TContext>(
        this IServiceCollection services,
        Action<OutboxServiceConfiguration> configuration
    ) where TContext : DbContext, IOutboxDbContext
    {
        services.AddScoped<IMessageDispatcher<OutboxMessage>, GeneratedDispatcher<OutboxMessage>>();
        SetupServices.SetupInternalOutboxServices<TContext>(services, configuration);

        return services;
    }

    /// <summary>
    /// Configures and registers Inbox services in the dependency injection container.
    /// </summary>
    /// <typeparam name=""TContext"">The DbContext type that implements IInboxDbContext.</typeparam>
    /// <param name=""services"">The service collection to register services with.</param>
    /// <param name=""configuration"">An action to configure the Inbox service settings.</param>
    public static IServiceCollection AddInboxServices<TContext>(
        this IServiceCollection services,
        Action<InboxServiceConfiguration> configuration
    ) where TContext : DbContext, IInboxDbContext
    {
        services.AddScoped<IMessageDispatcher<InboxMessage>, GeneratedDispatcher<InboxMessage>>();
        SetupServices.SetupInternalInboxServices<TContext>(services, configuration);

        return services;
    }
}
";
}
