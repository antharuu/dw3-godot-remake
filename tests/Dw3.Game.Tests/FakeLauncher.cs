using Dw3Production;

namespace Dw3.Game.Tests;

/// <summary>
/// Fake launcher that feeds canned lines synchronously.
/// No disc bytes or process spawning; all output is supplied by the test.
/// </summary>
internal sealed class FakeLauncher : IExtractorLauncher
{
    private readonly IEnumerable<string> _lines;
    private readonly int _exitCode;

    public FakeLauncher(IEnumerable<string> lines, int exitCode)
    {
        _lines = lines;
        _exitCode = exitCode;
    }

    public IExtractorProcess Launch(string discPath, string outputDirectory) =>
        new FakeProcess(_lines, _exitCode);
}
