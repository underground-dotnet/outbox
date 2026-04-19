using System.Runtime.CompilerServices;

using VerifyTests;

namespace Underground.Outbox.SourceGeneratorTest;

public static class VerifySourceGeneratorsSettings
{
    [ModuleInitializer]
    public static void Init()
    {
        Verifier.UseSourceFileRelativeDirectory("Snapshots");
        VerifySourceGenerators.Initialize();
    }
}
