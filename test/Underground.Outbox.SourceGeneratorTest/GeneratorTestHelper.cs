using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;

using Underground.Outbox.SourceGenerator;

namespace Underground.Outbox.SourceGeneratorTest;

internal static class GeneratorTestHelper
{
    /// <summary>
    /// Runs the generator over one source file, optionally against a referenced assembly compiled from
    /// <paramref name="referencedSource"/> - which is how cross-assembly handler discovery is exercised.
    /// </summary>
    public static GeneratorDriver Run(string source, string? referencedSource = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        List<MetadataReference> references = [.. BaseReferences()];
        if (referencedSource is not null)
        {
            references.Add(CompileReference(referencedSource));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree],
            references: references);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OutboxGenerator());

        return driver.RunGenerators(compilation);
    }

    private static PortableExecutableReference CompileReference(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ReferencedHandlers",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: BaseReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "The referenced assembly did not compile: "
                + string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    private static readonly string RuntimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    // EF Core is among them because the binding attribute's type parameter is constrained to DbContext
    private static IEnumerable<MetadataReference> BaseReferences() =>
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
        MetadataReference.CreateFromFile(Path.Combine(RuntimeDirectory, "System.Runtime.dll")),
        MetadataReference.CreateFromFile(typeof(DbContext).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(IOutboxMessageHandler<>).Assembly.Location),
    ];
}
