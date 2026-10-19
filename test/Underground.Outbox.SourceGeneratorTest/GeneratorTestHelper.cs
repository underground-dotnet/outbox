using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Attributes;
using Underground.Outbox.SourceGenerator;

namespace Underground.Outbox.SourceGeneratorTest;

internal static class GeneratorTestHelper
{
    public static GeneratorDriverRunResult Run(string source) => Run(source, referencedSource: null);

    /// <summary>Runs the generator on <paramref name="source"/> compiled against <paramref name="referencedSource"/>.</summary>
    /// <param name="source">The assembly the generator runs on.</param>
    /// <param name="referencedSource">An assembly <paramref name="source"/> references, which the generator does not run on.</param>
    public static GeneratorDriverRunResult Run(string source, string? referencedSource)
    {
        var references = GetReferences().ToList();

        if (referencedSource is not null)
        {
            var referenced = CSharpCompilation.Create(
                assemblyName: "ReferencedAssembly",
                syntaxTrees: [CSharpSyntaxTree.ParseText(referencedSource)],
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            references.Add(referenced.ToMetadataReference());
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: references);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OutboxGenerator());

        return driver.RunGenerators(compilation).GetRunResult();
    }

    /// <summary>Runs the generator on <paramref name="source"/>, then again after an unrelated file is added.</summary>
    public static GeneratorRunResult RunAfterUnrelatedEdit(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: GetReferences());

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new OutboxGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Sample; public sealed class Unrelated;"));

        return driver.RunGenerators(edited).GetRunResult().Results.Single();
    }

    /// <summary>
    /// The generator compares interface symbols rather than their spelling, so the compilation has to be
    /// able to resolve them - which takes the runtime assemblies as well as this library's own.
    /// </summary>
    private static IEnumerable<MetadataReference> GetReferences()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ServiceLifetime).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IOutboxMessageHandler<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MessageHandlerLifetimeAttribute).Assembly.Location),
        ];
    }
}
