using Xunit;

namespace FrameShift.Tests;

// WinForms, GDI and the process-wide theme cannot be exercised concurrently across test classes.
// Other collections keep their usual parallelism; this collection runs without overlap.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WinFormsTestCollection : ICollectionFixture<FrameShiftTestSettingsDirectory>
{
    public const string Name = "WinForms UI";
}
