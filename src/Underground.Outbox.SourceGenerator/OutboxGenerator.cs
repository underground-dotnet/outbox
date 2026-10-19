using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Underground.Outbox.SourceGenerator;

[Generator]
public sealed class OutboxGenerator : IIncrementalGenerator
{
    private const string OutboxHandlerInterface = "Underground.Outbox.IOutboxMessageHandler`1";
    private const string InboxHandlerInterface = "Underground.Outbox.IInboxMessageHandler`1";
    private const string LifetimeAttribute = "Underground.Outbox.Attributes.MessageHandlerLifetimeAttribute";

    private const string DefaultLifetime = "Transient";

    private static readonly DiagnosticDescriptor CompetingHandlers = new(
        id: "OUTBOX001",
        title: "Message type has competing handlers",
        messageFormat: "Message type '{0}' is handled by more than one {1} handler ({2}). Only one of them is dispatched.",
        category: "Underground.Outbox",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Handlers are discovered in the compiling assembly only. Every project that declares handlers
        // runs this generator and emits its own registration method, which the composition root calls.
        // Walking referenced assemblies instead would mean fetching AllInterfaces on every type in every
        // reference, which cannot be done incrementally.
        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s),
                transform: static (ctx, _) => GetSemanticTargetsForGeneration(ctx))
            .WithTrackingName("HandlerClasses")
            .SelectMany(static (handlers, _) => handlers)
            .Collect()
            .Select(static (handlers, _) => ToEquatableList(handlers));

        // the assembly name alone, never the Compilation itself, so this does not invalidate per keystroke
        var assemblyName = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName);

        context.RegisterSourceOutput(handlers.Combine(assemblyName), static (spc, source) =>
        {
            var (discovered, name) = source;
            Execute(discovered, name, spc);
        });
    }

    private static EquatableList<HandlerClassInfo> ToEquatableList(ImmutableArray<HandlerClassInfo> handlers)
    {
        var list = new EquatableList<HandlerClassInfo>();
        list.AddRange(handlers);
        return list;
    }

    /// <summary>
    /// A handler may inherit its interface from a base class, possibly in another assembly, so syntax cannot
    /// tell a handler apart; every class that could be one is left to the semantic check.
    /// </summary>
    private static bool IsSyntaxTargetForGeneration(SyntaxNode node) =>
        node is ClassDeclarationSyntax { BaseList: not null, TypeParameterList: null } classDecl
        && !classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword);

    private static EquatableList<HandlerClassInfo> GetSemanticTargetsForGeneration(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return [];
        }

        if (classSymbol.IsAbstract || IsOpenGeneric(classSymbol))
        {
            return [];
        }

        var compilation = context.SemanticModel.Compilation;
        var outbox = compilation.GetTypeByMetadataName(OutboxHandlerInterface);
        var inbox = compilation.GetTypeByMetadataName(InboxHandlerInterface);

        if (outbox is null && inbox is null)
        {
            return [];
        }

        var lifetime = ReadLifetime(classSymbol);
        var handlers = new EquatableList<HandlerClassInfo>();

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (!iface.IsGenericType)
            {
                continue;
            }

            var kind = ClassifyHandlerInterface(iface.OriginalDefinition, outbox, inbox);

            if (kind is null)
            {
                continue;
            }

            // fully qualified, so a user namespace cannot shadow a type argument's own namespace
            handlers.Add(new HandlerClassInfo(
                classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                kind.Value,
                lifetime));
        }

        return handlers;
    }

    /// <summary>
    /// A class with a type parameter of its own or of a containing type cannot be built by the container.
    /// </summary>
    private static bool IsOpenGeneric(INamedTypeSymbol classSymbol)
    {
        for (var type = classSymbol; type is not null; type = type.ContainingType)
        {
            if (type.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static HandlerKind? ClassifyHandlerInterface(INamedTypeSymbol definition, INamedTypeSymbol? outbox, INamedTypeSymbol? inbox)
    {
        if (SymbolEqualityComparer.Default.Equals(definition, outbox))
        {
            return HandlerKind.Outbox;
        }

        if (SymbolEqualityComparer.Default.Equals(definition, inbox))
        {
            return HandlerKind.Inbox;
        }

        return null;
    }

    /// <summary>
    /// Reads <c>[MessageHandlerLifetime]</c> off the handler or, as the attribute is inherited, its nearest base
    /// class that carries one. Without it a handler is Transient, which is what registration defaulted to when
    /// it was written by hand.
    /// </summary>
    private static string ReadLifetime(INamedTypeSymbol classSymbol)
    {
        for (var type = classSymbol; type is not null; type = type.BaseType)
        {
            if (FindLifetime(type) is { } lifetime)
            {
                return lifetime;
            }
        }

        return DefaultLifetime;
    }

    private static string? FindLifetime(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), LifetimeAttribute, StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not int value)
            {
                continue;
            }

            // Microsoft.Extensions.DependencyInjection.ServiceLifetime
            return value switch
            {
                0 => "Singleton",
                1 => "Scoped",
                2 => "Transient",
                _ => DefaultLifetime,
            };
        }

        return null;
    }

    private static void Execute(EquatableList<HandlerClassInfo> handlers, string? assemblyName, SourceProductionContext context)
    {
        var distinctHandlers = handlers.Distinct().ToList();

        if (distinctHandlers.Count == 0)
        {
            return;
        }

        ReportCompetingHandlers(distinctHandlers, context);

        var identifier = ToIdentifier(assemblyName);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();
        sb.AppendLine("using Underground.Outbox.Data;");
        sb.AppendLine("using Underground.Outbox.Domain.Dispatchers;");
        sb.AppendLine("using Underground.Outbox.Exceptions;");
        sb.AppendLine();
        sb.AppendLine("namespace Underground.Outbox.Configuration;");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
        sb.AppendLine($"public static class {identifier}HandlerRegistration");
        sb.AppendLine("{");
        AppendMethodSignature(sb, identifier, bindsInbox: distinctHandlers.Any(h => h.Kind == HandlerKind.Inbox));

        AppendHandlerRegistrations(sb, distinctHandlers);
        sb.AppendLine();

        foreach (var handler in distinctHandlers)
        {
            AppendEntry(sb, handler);
            sb.AppendLine();
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine("#pragma warning restore CS1591");

        context.AddSource("OutboxHandlerRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Opens the registration method. An assembly's inbox handlers all serve one Inbox, which the composition
    /// root names, so the method takes that name only when the assembly declares any.
    /// </summary>
    private static void AppendMethodSignature(StringBuilder sb, string identifier, bool bindsInbox)
    {
        sb.AppendLine($"    /// <summary>Registers the message handlers declared in the {identifier} assembly.</summary>");
        if (!bindsInbox)
        {
            sb.AppendLine($"    public static IServiceCollection Add{identifier}MessageHandlers(this IServiceCollection services)");
            sb.AppendLine("    {");
            return;
        }

        sb.AppendLine("    /// <param name=\"services\">The service collection to register the handlers with.</param>");
        sb.AppendLine("    /// <param name=\"inbox\">The Inbox whose messages this assembly's inbox handlers run.</param>");
        sb.AppendLine($"    public static IServiceCollection Add{identifier}MessageHandlers(this IServiceCollection services, string inbox)");
        sb.AppendLine("    {");
        sb.AppendLine("        ArgumentException.ThrowIfNullOrWhiteSpace(inbox);");
        sb.AppendLine();
    }

    /// <summary>
    /// Registers each handler once as its concrete type, and points every outbox handler interface it
    /// implements at that one registration. Registering each interface independently would give a Scoped
    /// handler of two message types two instances per scope. Inbox handler interfaces are not registered:
    /// two Inboxes may each have a handler for one message type, so the interface names no single handler.
    /// </summary>
    private static void AppendHandlerRegistrations(StringBuilder sb, IEnumerable<HandlerClassInfo> handlers)
    {
        foreach (var handler in handlers.GroupBy(h => h.HandlerFullName, StringComparer.Ordinal))
        {
            var lifetime = handler.First().Lifetime;

            sb.AppendLine($"        services.TryAdd{lifetime}<{handler.Key}>();");

            foreach (var registration in handler.Where(h => h.Kind == HandlerKind.Outbox))
            {
                var iface = InterfaceName(registration);
                sb.AppendLine($"        services.TryAdd{lifetime}<{iface}>(static serviceProvider => serviceProvider.GetRequiredService<{registration.HandlerFullName}>());");
            }
        }
    }

    private static void AppendEntry(StringBuilder sb, HandlerClassInfo handler)
    {
        var entity = handler.Kind == HandlerKind.Inbox ? "InboxMessage" : "OutboxMessage";
        var messageType = handler.MessageTypeDisplayName;
        var iface = InterfaceName(handler);

        sb.AppendLine($"        services.AddSingleton(new HandlerEntry<{entity}>(");
        if (handler.Kind == HandlerKind.Inbox)
        {
            sb.AppendLine("            inbox,");
        }

        // the runtime spelling, which is what the write side stores; a literal would differ for nested and generic types
        sb.AppendLine($"            typeof({messageType}).FullName!,");
        sb.AppendLine($"            typeof({handler.HandlerFullName}),");
        sb.AppendLine($"            typeof({messageType}),");
        sb.AppendLine("            static async (serviceProvider, message, metadata, cancellationToken) =>");
        sb.AppendLine("            {");
        sb.AppendLine($"                var payload = JsonSerializer.Deserialize<{messageType}>(message.Data)");
        // the id only: the body is application data, and this message ends up in logs and on spans
        sb.AppendLine("                    ?? throw new ParsingException($\"Cannot parse event body of message: {message.Id}\");");
        // an inbox entry resolves its own handler, since the inbox interface is not registered
        sb.AppendLine(handler.Kind == HandlerKind.Inbox
            ? $"                {iface} handler = serviceProvider.GetRequiredService<{handler.HandlerFullName}>();"
            : $"                var handler = serviceProvider.GetRequiredService<{iface}>();");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine("                    await handler.HandleAsync(payload, metadata, cancellationToken);");
        sb.AppendLine("                }");
        // only a cancellation of the token it was given travels on as one; a Handler's own, such as an
        // HttpClient timeout, is an ordinary failure
        sb.AppendLine("                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)");
        sb.AppendLine("                {");
        sb.AppendLine("                    throw new MessageHandlerException(");
        sb.AppendLine("                        handler.GetType(),");
        sb.AppendLine($"                        typeof({messageType}),");
        sb.AppendLine("                        $\"Error processing message {message.Id} with handler {handler.GetType().Name}\",");
        sb.AppendLine("                        ex");
        sb.AppendLine("                    );");
        sb.AppendLine("                }");
        sb.AppendLine("            }));");
    }

    private static string InterfaceName(HandlerClassInfo handler)
    {
        var iface = handler.Kind == HandlerKind.Inbox ? "IInboxMessageHandler" : "IOutboxMessageHandler";

        return $"global::Underground.Outbox.{iface}<{handler.MessageTypeDisplayName}>";
    }

    /// <summary>
    /// The assembly name reduced to something that can sit inside a method name, so that two modules
    /// running this generator do not emit the same type.
    /// </summary>
    private static string ToIdentifier(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return "Outbox";
        }

        var sb = new StringBuilder(assemblyName!.Length);

        foreach (var c in assemblyName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        if (sb.Length == 0 || char.IsDigit(sb[0]))
        {
            sb.Insert(0, "Outbox");
        }

        return char.ToUpper(sb[0], CultureInfo.InvariantCulture) + sb.ToString(1, sb.Length - 1);
    }

    /// <summary>
    /// Reports message types claimed by more than one handler of the same kind within this assembly. Two
    /// handlers in different assemblies cannot be seen by one compilation; the Handler Registry catches
    /// that pair when the host starts.
    /// </summary>
    private static string Unqualified(string fullyQualifiedName) =>
        fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal)
            ? fullyQualifiedName.Substring("global::".Length)
            : fullyQualifiedName;

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
                Unqualified(group.Key.MessageTypeDisplayName),
                group.Key.Kind == HandlerKind.Inbox ? "inbox" : "outbox",
                string.Join(", ", group.Select(h => Unqualified(h.HandlerFullName)).OrderBy(n => n, StringComparer.Ordinal))
            ));
        }
    }
}
