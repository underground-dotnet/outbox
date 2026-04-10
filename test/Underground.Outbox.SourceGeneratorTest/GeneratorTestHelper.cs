using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Underground.Outbox.SourceGenerator;

namespace Underground.Outbox.SourceGeneratorTest;

internal static class GeneratorTestHelper
{
    public static GeneratorTestResult Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree],
            references: GetReferences());

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OutboxGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal);

        return new GeneratorTestResult(
            generatedSources,
            diagnostics,
            outputCompilation.GetDiagnostics());
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        // var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<MetadataReference> references = [
            MetadataReference.CreateFromFile(typeof(IOutboxMessageHandler<>).Assembly.Location)
        ];

        return references;
    }
}

internal sealed record GeneratorTestResult(
    IReadOnlyDictionary<string, string> GeneratedSources,
    ImmutableArray<Diagnostic> DriverDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics)
{
    public string GetGeneratedSource(string hintName)
    {
        return GeneratedSources.TryGetValue(hintName, out var source)
            ? source
            : throw new Xunit.Sdk.XunitException($"Generated source '{hintName}' was not found.");
    }
}
