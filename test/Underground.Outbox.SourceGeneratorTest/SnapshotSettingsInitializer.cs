using System.Runtime.CompilerServices;

using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.SnapshotTesting.Roslyn;

namespace Underground.Outbox.SourceGeneratorTest;

internal static class SnapshotSettingsInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddRoslyn();
    }
}
