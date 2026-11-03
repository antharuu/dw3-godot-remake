using Dw3Production;

namespace Dw3.Game.Tests;

/// <summary>
/// Fake extractor process that blocks at the first ReadLine call until a release gate is set.
/// Signals <paramref name="started"/> immediately when ReadLine is first entered, then waits
/// on <paramref name="release"/>. After unblocking it returns null (EOF), which triggers
/// the "ended without complete" error path in ExtractionController.
/// </summary>
/// <param name="started">Signalled when ReadLine is first entered to indicate Extracting state.</param>
/// <param name="release">Waited on by ReadLine; set by the test to unblock the process.</param>
internal sealed class LatchedProcess(ManualResetEventSlim started, ManualResetEventSlim release)
    : IExtractorProcess
{
    private bool _firstRead = true;

    public string? ReadLine()
    {
        if (_firstRead)
        {
            _firstRead = false;
            started.Set();     // Signal that Extracting state is active.
            release.Wait();    // Block until the test releases us.
        }

        return null;  // EOF: triggers the "ended without complete" error path.
    }

    public int WaitForExit() => 0;

    public void Dispose()
    {
    }
}
