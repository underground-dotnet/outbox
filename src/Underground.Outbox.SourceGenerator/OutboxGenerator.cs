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
    // when using CSharpCompilation vs CompilationProvider we loose some information from the type. Therefore we need to use < instead of <T>.
    private const string OutboxHandlerInterface = "Underground.Outbox.IOutboxMessageHandler<";
    private const string InboxHandlerInterface = "Underground.Outbox.IInboxMessageHandler<";
    private const string MarkerAttributeFullName = "Underground.Outbox.Attributes.ContainsOutboxHandlersAttribute";

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
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null)
            // convert to non-nullable
            .Select(static (m, _) => m!.Value)
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

    private static HandlerClassInfo? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        if (classSymbol.IsAbstract)
        {
            return null;
        }

        return GetHandlerInfo(classSymbol);
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
            .Any(a => a.AttributeClass?.ToDisplayString() == MarkerAttributeFullName);

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
                var info = GetHandlerInfo(type);
                if (info.HasValue)
                {
                    handlers.Add(info.Value);
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
    private static HandlerClassInfo? GetHandlerInfo(INamedTypeSymbol typeSymbol)
    {
        foreach (var iface in typeSymbol.Interfaces)
        {
            if (!iface.IsGenericType)
                continue;

            var originalDef = iface.OriginalDefinition.ToDisplayString();

            if (originalDef.StartsWith(OutboxHandlerInterface, StringComparison.Ordinal))
            {
                return new HandlerClassInfo(
                    typeSymbol.ToDisplayString(),
                    iface.TypeArguments[0].ToDisplayString(),
                    HandlerKind.Outbox
                );
            }

            if (originalDef.StartsWith(InboxHandlerInterface, StringComparison.Ordinal))
            {
                return new HandlerClassInfo(
                    typeSymbol.ToDisplayString(),
                    iface.TypeArguments[0].ToDisplayString(),
                    HandlerKind.Inbox
                );
            }
        }

        return null;

        // foreach (var baseType in classDeclaration.BaseList!.Types)
        // {
        //     var typeInfo = context.SemanticModel.GetTypeInfo(baseType.Type);
        //     if (typeInfo.Type is INamedTypeSymbol typeSymbol
        //     && typeSymbol.IsGenericType
        //     && typeSymbol.OriginalDefinition.ToDisplayString() == "Underground.Outbox.IOutboxMessageHandler<T>")
        //     {
        //         return new HandlerClassInfo(
        //             typeSymbol.ContainingNamespace.ToDisplayString(),
        //             classDeclaration.Identifier.Text,
        //             // typeSymbol.OriginalDefinition.ToDisplayString(),
        //             typeSymbol.TypeArguments[0].ToDisplayString()
        //         );
        //     }
        // }
    }

    private static void Execute(EquatableList<HandlerClassInfo> handlers, SourceProductionContext context)
    {
        var kindInbox = handlers.Where(h => h.Kind == HandlerKind.Inbox).ToList();
        var kindOutbox = handlers.Where(h => h.Kind == HandlerKind.Outbox).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
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

        sb.AppendLine($"// {DateTime.UtcNow}");
        sb.AppendLine("        var metadata = new MessageMetadata(message.EventId, message.PartitionKey, message.RetryCount);");
        sb.AppendLine("        var serviceProvider = scope.ServiceProvider;");
        sb.AppendLine("        if (typeof(TMessage) == typeof(InboxMessage))");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (message.Type)");
        sb.AppendLine("            {");
        foreach (var classInfo in kindInbox)
        {
            sb.AppendLine($"                case \"{classInfo.MessageTypeFullName}\":");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var fullEvent = JsonSerializer.Deserialize<{classInfo.MessageTypeFullName}>(message.Data) ?? throw new ParsingException($\"Cannot parse event body {{message.Data}} of message: {{message.Id}}\");");
            sb.AppendLine($"                    var handler = serviceProvider.GetRequiredService<IInboxMessageHandler<{classInfo.MessageTypeFullName}>>();");
            sb.AppendLine("                    try");
            sb.AppendLine("                    {");
            sb.AppendLine("                        await handler.HandleAsync(fullEvent, metadata, cancellationToken);");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    catch (Exception ex)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        throw new MessageHandlerException(");
            sb.AppendLine("                            handler.GetType(),");
            sb.AppendLine("                            $\"Error processing message {message.Id} with handler {handler.GetType().Name}\",");
            sb.AppendLine("                            ex");
            sb.AppendLine("                        );");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
        }
        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new ParsingException($\"No handler configured for message type {message.Type} of message: {message.Id}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        if (typeof(TMessage) == typeof(OutboxMessage))");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (message.Type)");
        sb.AppendLine("            {");
        foreach (var classInfo in kindOutbox)
        {
            sb.AppendLine($"                case \"{classInfo.MessageTypeFullName}\":");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var fullEvent = JsonSerializer.Deserialize<{classInfo.MessageTypeFullName}>(message.Data) ?? throw new ParsingException($\"Cannot parse event body {{message.Data}} of message: {{message.Id}}\");");
            sb.AppendLine($"                    var handler = serviceProvider.GetRequiredService<IOutboxMessageHandler<{classInfo.MessageTypeFullName}>>();");
            sb.AppendLine("                    try");
            sb.AppendLine("                    {");
            sb.AppendLine("                        await handler.HandleAsync(fullEvent, metadata, cancellationToken);");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
            sb.AppendLine("                    catch (Exception ex)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        throw new MessageHandlerException(");
            sb.AppendLine("                            handler.GetType(),");
            sb.AppendLine("                            $\"Error processing message {message.Id} with handler {handler.GetType().Name}\",");
            sb.AppendLine("                            ex");
            sb.AppendLine("                        );");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
        }
        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new ParsingException($\"No handler configured for message type {message.Type} of message: {message.Id}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        throw new ParsingException($\"Unsupported dispatcher message type {typeof(TMessage).FullName}\");");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("#pragma warning restore CS1591");

        context.AddSource("GeneratedDispatcher.g.cs", sb.ToString());
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
        SetupOutboxServices.SetupInternalOutboxServices<TContext>(services, configuration);

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
        SetupOutboxServices.SetupInternalInboxServices<TContext>(services, configuration);

        return services;
    }
}
";

    // https://andrewlock.net/creating-a-source-generator-part-5-finding-a-type-declarations-namespace-and-type-hierarchy/
    // static string GetNamespace(BaseTypeDeclarationSyntax syntax)
    // {
    //     string nameSpace = string.Empty;

    //     // Get the containing syntax node for the type declaration
    //     // (could be a nested type, for example)
    //     SyntaxNode? potentialNamespaceParent = syntax.Parent;

    //     // Keep moving "out" of nested classes etc until we get to a namespace
    //     // or until we run out of parents
    //     while (potentialNamespaceParent != null &&
    //             potentialNamespaceParent is not NamespaceDeclarationSyntax
    //             && potentialNamespaceParent is not FileScopedNamespaceDeclarationSyntax)
    //     {
    //         potentialNamespaceParent = potentialNamespaceParent.Parent;
    //     }

    //     // Build up the final namespace by looping until we no longer have a namespace declaration
    //     if (potentialNamespaceParent is BaseNamespaceDeclarationSyntax namespaceParent)
    //     {
    //         // We have a namespace. Use that as the type
    //         nameSpace = namespaceParent.Name.ToString();

    //         // Keep moving "out" of the namespace declarations until we
    //         // run out of nested namespace declarations
    //         while (true)
    //         {
    //             if (namespaceParent.Parent is not NamespaceDeclarationSyntax parent)
    //             {
    //                 break;
    //             }

    //             // Add the outer namespace as a prefix to the final namespace
    //             nameSpace = $"{namespaceParent.Name}.{nameSpace}";
    //             namespaceParent = parent;
    //         }
    //     }

    //     // return the final namespace
    //     return nameSpace;
    // }

}
