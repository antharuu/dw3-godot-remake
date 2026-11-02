using Dw3Production;

namespace Dw3.Game.Tests;

/// <summary>
/// Fake extractor process that replays a fixed sequence of output lines
/// and returns a predetermined exit code. No real process is spawned.
/// </summary>
internal sealed class FakeProcess : IExtractorProcess
{
    private readonly IEnumerator<string> _lines;
    private readonly int _exitCode;

    public FakeProcess(IEnumerable<string> lines, int exitCode)
    {
        _lines = lines.GetEnumerator();
        _exitCode = exitCode;
    }

    public string? ReadLine() => _lines.MoveNext() ? _lines.Current : null;

    public int WaitForExit() => _exitCode;

    public void Dispose() => _lines.Dispose();
}
