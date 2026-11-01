using Dw3Production;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Unit tests for ExtractionController using a fake extractor process.
/// All lines are fabricated; no disc bytes are read.
/// </summary>
public sealed class ExtractionControllerTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ExtractionController Build(IEnumerable<string> lines, int exitCode = 0)
    {
        var launcher = new FakeLauncher(lines, exitCode);
        return new ExtractionController(launcher);
    }

    private static string Progress(string domain, int done, int total) =>
        $"{{\"step\":\"{domain}\",\"done\":{done},\"total\":{total}}}";

    private static string Complete(int domains = 3) =>
        $"{{\"event\":\"complete\",\"domains\":{domains}}}";

    private static string Error(string code, string message) =>
        $"{{\"event\":\"error\",\"code\":\"{code}\",\"message\":\"{message}\"}}";

    // ---------------------------------------------------------------------------
    // Initial state
    // ---------------------------------------------------------------------------

    [Fact]
    public void InitialStateIsIdle()
    {
        var controller = Build([]);
        Assert.Equal(ExtractionState.Idle, controller.State);
    }

    // ---------------------------------------------------------------------------
    // Happy path: progress → complete
    // ---------------------------------------------------------------------------

    [Fact]
    public void ExtractingStateWhileRunning()
    {
        // Verify that State == Extracting while the background process is running.
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var launcher = new LatchedFakeLauncher(started, release);
        var controller = new ExtractionController(launcher);

        ExtractionState? observed = null;
        var thread = new Thread(() =>
        {
            started.Wait();
            observed = controller.State;
            release.Set();
        })
        { IsBackground = true };
        thread.Start();

        controller.Extract("/fake/disc.cue", "/fake/out");
        thread.Join(TimeSpan.FromSeconds(5));

        Assert.Equal(ExtractionState.Extracting, observed);
    }

    [Fact]
    public void CompletionEventFiredAndStateIsComplete()
    {
        var lines = new[]
        {
            Progress("zones", 1, 3),
            Progress("collision", 2, 3),
            Progress("player", 3, 3),
            Complete(3),
        };
        var controller = Build(lines);
        bool completedFired = false;
        controller.Completed += () => completedFired = true;

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.True(completedFired);
        Assert.Equal(ExtractionState.Complete, controller.State);
    }

    [Fact]
    public void ProgressEventsDeliveredInOrder()
    {
        var lines = new[]
        {
            Progress("zones", 1, 3),
            Progress("collision", 2, 3),
            Progress("player", 3, 3),
            Complete(3),
        };
        var controller = Build(lines);
        var received = new List<(string domain, int done, int total)>();
        controller.ProgressUpdated += (d, done, total) => received.Add((d, done, total));

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.Equal(3, received.Count);
        Assert.Equal(("zones", 1, 3), received[0]);
        Assert.Equal(("collision", 2, 3), received[1]);
        Assert.Equal(("player", 3, 3), received[2]);
    }

    // ---------------------------------------------------------------------------
    // Error path
    // ---------------------------------------------------------------------------

    [Fact]
    public void ErrorEventFiredAndStateIsError()
    {
        var lines = new[]
        {
            Progress("zones", 1, 3),
            Error("DISC_READ_FAILED", "Cannot read sector 42."),
        };
        var controller = Build(lines);
        string? errorMessage = null;
        controller.Failed += msg => errorMessage = msg;

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.Equal(ExtractionState.Error, controller.State);
        Assert.Equal("Cannot read sector 42.", errorMessage);
    }

    [Fact]
    public void NonZeroExitCodeWithoutCompleteIsError()
    {
        var lines = new[] { Progress("zones", 1, 3) };
        var controller = Build(lines, exitCode: 1);
        string? errorMessage = null;
        controller.Failed += msg => errorMessage = msg;

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.Equal(ExtractionState.Error, controller.State);
        Assert.NotNull(errorMessage);
        Assert.Contains("1", errorMessage);
    }

    [Fact]
    public void EofWithoutCompleteAndZeroExitIsError()
    {
        var lines = new[] { Progress("zones", 1, 3) };
        var controller = Build(lines, exitCode: 0);
        string? errorMessage = null;
        controller.Failed += msg => errorMessage = msg;

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.Equal(ExtractionState.Error, controller.State);
        Assert.NotNull(errorMessage);
    }

    // ---------------------------------------------------------------------------
    // Schema-mismatch: DataPackException is treated as "pack absent" upstream.
    // The controller itself does not throw; the caller (GameHost) catches
    // DataPackException before showing the screen. This test covers the contract.
    // ---------------------------------------------------------------------------

    [Fact]
    public void NonJsonDiagnosticLinesAreIgnored()
    {
        var lines = new[]
        {
            "Building project...",           // diagnostic non-JSON
            Progress("zones", 1, 1),
            "Compilation finished.",         // another non-JSON line
            Complete(1),
        };
        var controller = Build(lines);
        bool completedFired = false;
        controller.Completed += () => completedFired = true;

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.True(completedFired);
        Assert.Equal(ExtractionState.Complete, controller.State);
    }

    [Fact]
    public void StopsProcessingAfterErrorLine()
    {
        var lines = new[]
        {
            Error("DISC_READ_FAILED", "sector error"),
            // These lines must not be processed after error:
            Progress("collision", 2, 3),
            Complete(3),
        };
        var controller = Build(lines);
        var progressEvents = new List<string>();
        controller.ProgressUpdated += (d, _, _) => progressEvents.Add(d);

        controller.Extract("/fake/disc.cue", "/fake/out");

        Assert.Equal(ExtractionState.Error, controller.State);
        Assert.Empty(progressEvents);
    }

    [Fact]
    public void ThrowsIfAlreadyExtracting()
    {
        // Use a latch to gate the extraction thread at the first ReadLine call,
        // ensuring we observe the Extracting state before the assertion.
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var launcher = new LatchedFakeLauncher(started, release);
        var controller = new ExtractionController(launcher);

        var thread = new Thread(() => controller.Extract("/fake/disc.cue", "/fake/out"))
        { IsBackground = true };
        thread.Start();

        // Block until the extraction thread has entered Extracting state.
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "Extraction thread did not start in time.");
        Assert.Equal(ExtractionState.Extracting, controller.State);

        // A concurrent call on the same controller must throw.
        Assert.Throws<InvalidOperationException>(() => controller.Extract("/fake/disc.cue", "/fake/out"));

        // Unblock the extraction thread.
        release.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Extraction thread did not finish in time.");
    }
}

// ---------------------------------------------------------------------------
// Fake infrastructure — no disc bytes, no process spawning
// ---------------------------------------------------------------------------

/// <summary>
/// Fake launcher that feeds canned lines synchronously.
/// </summary>
file sealed class FakeLauncher : IExtractorLauncher
{
    private readonly IEnumerable<string> _lines;
    private readonly int _exitCode;
    public FakeLauncher(IEnumerable<string> lines, int exitCode) { _lines = lines; _exitCode = exitCode; }
    public IExtractorProcess Launch(string discPath, string outputDirectory) => new FakeProcess(_lines, _exitCode);
}

file sealed class FakeProcess : IExtractorProcess
{
    private readonly IEnumerator<string> _lines;
    private readonly int _exitCode;
    public FakeProcess(IEnumerable<string> lines, int exitCode) { _lines = lines.GetEnumerator(); _exitCode = exitCode; }
    public string? ReadLine() => _lines.MoveNext() ? _lines.Current : null;
    public int WaitForExit() => _exitCode;
    public void Dispose() => _lines.Dispose();
}

/// <summary>
/// Fake launcher whose process blocks at the first ReadLine until the release gate is set.
/// Signals <paramref name="started"/> as soon as ReadLine is first called.
/// </summary>
file sealed class LatchedFakeLauncher : IExtractorLauncher
{
    private readonly ManualResetEventSlim _started;
    private readonly ManualResetEventSlim _release;
    public LatchedFakeLauncher(ManualResetEventSlim started, ManualResetEventSlim release)
    {
        _started = started;
        _release = release;
    }
    public IExtractorProcess Launch(string discPath, string outputDirectory) =>
        new LatchedProcess(_started, _release);
}

file sealed class LatchedProcess : IExtractorProcess
{
    private readonly ManualResetEventSlim _started;
    private readonly ManualResetEventSlim _release;
    private bool _firstRead = true;
    public LatchedProcess(ManualResetEventSlim started, ManualResetEventSlim release)
    {
        _started = started;
        _release = release;
    }
    public string? ReadLine()
    {
        if (_firstRead)
        {
            _firstRead = false;
            _started.Set();     // Signal that Extracting state is active.
            _release.Wait();    // Block until the test releases us.
        }
        return null;  // EOF: triggers the "ended without complete" error path.
    }
    public int WaitForExit() => 0;
    public void Dispose() { }
}
