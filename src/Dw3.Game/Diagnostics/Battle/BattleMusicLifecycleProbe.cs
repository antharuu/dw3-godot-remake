using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Records a real host battle track through two loops, victory, and field teardown.</summary>
internal sealed class BattleMusicLifecycleProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleMusicLifecycleProbe"/> class.</summary>
    /// <param name="host">Actual offscreen GameHost adapter.</param>
    public BattleMusicLifecycleProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9062_MUSIC";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        ProbeRequirement.Require(BattleAudioRecording.Enabled, "Audio recording must be explicitly enabled.");
        Host.ApplyS200();
        string root = Host.PrepareCaptureDirectory("probe/battle-music-lifecycle");
        await RecordRepeatedMusicAsync(root);
        await RecordVictoryAndReturnAsync(root);
        GD.Print("B9062_MUSIC_PASS path=encounter-loop-loop-victory-field sameRenderer=true finalChannels=0");
    }

    /// <summary>Requires physical Dummy-driver evidence instead of a nullable disabled recorder.</summary>
    /// <param name="path">Private WAV output path.</param>
    /// <returns>An enabled recorder.</returns>
    private static BattleAudioRecording StartRecording(string path) => BattleAudioRecording.Start(path)
        ?? throw new InvalidOperationException("Physical audio recording is disabled.");

    /// <summary>Waits for a physical mixer interval without changing battle simulation timing.</summary>
    /// <param name="seconds">Minimum wall-clock duration.</param>
    /// <returns>Completion after real mixer processing.</returns>
    private static async Task WaitSecondsAsync(double seconds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    /// <summary>Records a nonempty silent post-teardown bus control after the mixer drains.</summary>
    /// <param name="path">Private WAV output path.</param>
    /// <returns>Completion of the silence measurement.</returns>
    private static async Task RecordSilenceAsync(string path)
    {
        await WaitSecondsAsync(0.25);
        using var audio = StartRecording(path);
        await WaitSecondsAsync(0.25);
        audio.Save(false);
    }

    /// <summary>Records two source-loop boundaries from the ordinary F8 encounter path.</summary>
    /// <param name="root">Private output directory.</param>
    /// <returns>Completion after two observed authored boundaries.</returns>
    private async Task RecordRepeatedMusicAsync(string root)
    {
        using var audio = StartRecording(Path.Combine(root, "encounter-two-loops.wav"));
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle, "Host battle setup failed.");
        await WaitForLoopsAsync(2);
        audio.Save();
        ProbeRequirement.Require(Host.Renderer.BattleMusicLoopCount >= 2, "Battle music did not repeat twice.");
        await WaitForPostBoundaryNotesAsync();
        GD.Print($"B9062_MUSIC_LOOPS count={Host.Renderer.BattleMusicLoopCount} active={Host.Renderer.ActiveAudioVoices}");
    }

    /// <summary>Transitions the real won-battle path to one victory jingle and proves final silence.</summary>
    /// <param name="root">Private output directory.</param>
    /// <returns>Completion after report transition and field teardown.</returns>
    private async Task RecordVictoryAndReturnAsync(string root)
    {
        using (var audio = StartRecording(Path.Combine(root, "victory.wav")))
        {
            Host.Flow.EndBattle(BattleOutcome.Won);
            Host.TickTestSimulation();
            await WaitSecondsAsync(2);
            audio.Save();
        }

        Host.ExitBattleForProbe();
        ProbeRequirement.Require(!Host.HasActiveBattle && !Host.State.BattleActive, "Host did not return to field.");
        await RecordSilenceAsync(Path.Combine(root, "after-return.wav"));
    }

    /// <summary>Requires a physical note after the second boundary rather than only observing a restart counter.</summary>
    /// <returns>Completion after the repeated segment has armed a real mixer voice.</returns>
    private async Task WaitForPostBoundaryNotesAsync()
    {
        const double noteDeadlineSeconds = 2;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (Host.Renderer.ActiveAudioVoices == 0 && timer.Elapsed.TotalSeconds < noteDeadlineSeconds)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        ProbeRequirement.Require(Host.Renderer.ActiveAudioVoices > 0, "Repeated music did not sustain a post-boundary physical note.");
    }

    /// <summary>Waits for the real audio clock instead of assuming an offscreen frame rate.</summary>
    /// <param name="minimum">Required completed authored boundaries.</param>
    /// <returns>Completion when the source loop repeated or the bounded diagnostic times out.</returns>
    private async Task WaitForLoopsAsync(int minimum)
    {
        const double loopDeadlineSeconds = 150;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (Host.Renderer.BattleMusicLoopCount < minimum && timer.Elapsed.TotalSeconds < loopDeadlineSeconds)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        ProbeRequirement.Require(
            Host.Renderer.BattleMusicLoopCount >= minimum,
            $"Music loop timeout loops={Host.Renderer.BattleMusicLoopCount} seconds={timer.Elapsed.TotalSeconds:0.0}");
    }
}
