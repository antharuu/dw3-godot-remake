using Dw3Production;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class SystemExtractorLauncherTests
{
    [Fact]
    public void LegacyExtractorFailureIsReportedToController()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var controller = new ExtractionController(new SystemExtractorLauncher("/bin/false"));
        string? failure = null;
        controller.Failed += message => failure = message;
        controller.Extract("/fake/disc.cue", "/fake/out");
        Assert.Equal(ExtractionState.Error, controller.State);
        Assert.Contains("schema", failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OldSchemaIsRejectedBeforeExtractCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), $"dw3-old-extractor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string executable = Path.Combine(root, "extractor");
        string marker = Path.Combine(root, "extracted");
        try
        {
            File.WriteAllText(executable, $"#!/bin/sh\nif [ \"$1\" = --schema-version ]; then printf '16\\n'; else touch '{marker}'; fi\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var launcher = new SystemExtractorLauncher(executable);
            Assert.Throws<InvalidOperationException>(() => launcher.Launch("disc", "out"));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyExtractorIsRejectedBeforeExtraction()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var launcher = new SystemExtractorLauncher("/bin/false");
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => launcher.Launch("/fake/disc.cue", "/fake/out"));
        Assert.Contains("schema", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
