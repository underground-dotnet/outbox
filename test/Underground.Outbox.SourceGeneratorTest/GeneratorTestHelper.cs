using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

using Underground.Outbox.Attributes;
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
