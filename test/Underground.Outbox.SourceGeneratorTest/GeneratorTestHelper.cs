using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Underground.Outbox.SourceGenerator;

namespace Underground.Outbox.SourceGeneratorTest;

internal static class GeneratorTestHelper
{
    public static GeneratorDriver Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTests",
            syntaxTrees: [syntaxTree],
            references: GetReferences());

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new OutboxGenerator());

        return driver.RunGenerators(compilation);
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        IEnumerable<MetadataReference> references = [
            MetadataReference.CreateFromFile(typeof(IOutboxMessageHandler<>).Assembly.Location)
        ];

        return references;
    }
}
