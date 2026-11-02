using Dw3Production;

namespace Dw3.Game.Tests;

/// <summary>
/// Fake launcher whose process blocks at the first ReadLine call until a release gate is set.
/// The <paramref name="started"/> event is signalled as soon as ReadLine is first called,
/// letting tests verify the Extracting state before unblocking.
/// </summary>
/// <param name="started">Set by the process when the first ReadLine call is entered.</param>
/// <param name="release">Set by the test to let the process proceed past the first ReadLine.</param>
internal sealed class LatchedFakeLauncher(ManualResetEventSlim started, ManualResetEventSlim release)
    : IExtractorLauncher
{
    public IExtractorProcess Launch(string discPath, string outputDirectory) =>
        new LatchedProcess(started, release);
}
