using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Records a real host battle track through two loops with post-boundary notes and its authored
/// reverb, the production victory transition, field return with a drained tail, then a second
/// encounter cancelled mid-music, each ending in a nonempty silent mix.
/// </summary>
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
        await RecordReentryAndCancelAsync(root);
        GD.Print($"B9062_MUSIC_PASS driver={AudioServer.GetDriverName()} path=encounter-loop-loop-victory-field-reentry-cancel sameRenderer=true victoryStarts={Host.Renderer.VictoryAudioStarts} finalChannels={Host.Renderer.ActiveAudioVoices}");
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

    /// <summary>Lets the reverb tail drain, then records a nonempty silent bus control.</summary>
    /// <param name="path">Private output WAV.</param>
    /// <returns>Completion of the silence measurement.</returns>
    private static async Task RecordSilenceAfterTailAsync(string path)
    {
        const double tailDrainSeconds = 3;
        await WaitSecondsAsync(tailDrainSeconds);
        using var audio = StartRecording(path);
        await WaitSecondsAsync(0.5);
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
        await WaitForPostBoundaryNotesAsync();
        audio.Save();
        var reverb = Host.Renderer.BattleReverb;
        GD.Print($"B9062_MUSIC_LOOPS count={Host.Renderer.BattleMusicLoopCount} active={Host.Renderer.ActiveAudioVoices} reverbType={reverb.Type} reverbDepth={reverb.Depth}");
        ProbeRequirement.Require(reverb == (3, 56), "Battle music did not apply its authored reverb type 3 depth 56.");
    }

    /// <summary>Transitions the real won-battle path to one victory track and proves the drained return.</summary>
    /// <param name="root">Private output directory.</param>
    /// <returns>Completion after report transition and field teardown.</returns>
    private async Task RecordVictoryAndReturnAsync(string root)
    {
        using (var audio = StartRecording(Path.Combine(root, "victory.wav")))
        {
            Host.Flow.EndBattle(BattleOutcome.Won);
            Host.TickTestSimulation();
            Host.TickTestSimulation();
            await WaitSecondsAsync(2);
            audio.Save();
        }

        var reverb = Host.Renderer.BattleReverb;
        GD.Print($"B9062_MUSIC_VICTORY starts={Host.Renderer.VictoryAudioStarts} reverbType={reverb.Type} reverbDepth={reverb.Depth}");
        ProbeRequirement.Require(Host.Renderer.VictoryAudioStarts == 1, "Production victory did not start exactly once.");
        ProbeRequirement.Require(reverb == (3, 64), "Victory track did not apply its authored reverb depth 64.");
        Host.ExitBattleForProbe();
        ProbeRequirement.Require(!Host.HasActiveBattle && !Host.State.BattleActive, "Host did not return to field.");
        await RecordSilenceAfterTailAsync(Path.Combine(root, "after-return.wav"));
    }

    /// <summary>Starts a second encounter on the same renderer, cancels it mid-music and proves silence.</summary>
    /// <param name="root">Private output directory.</param>
    /// <returns>Completion after the cancelled battle drained.</returns>
    private async Task RecordReentryAndCancelAsync(string root)
    {
        using (var audio = StartRecording(Path.Combine(root, "reentry.wav")))
        {
            Host.StartDebugBattle();
            ProbeRequirement.Require(Host.HasActiveBattle && Host.Renderer.BattleMusicLoopCount == 0, "Re-entry did not restart the battle track.");
            await WaitForPostBoundaryNotesAsync();
            await WaitSecondsAsync(1.5);
            audio.Save();
        }

        Host.CancelSequenceForProbe();
        Host.ExitBattleForProbe();
        ProbeRequirement.Require(!Host.HasActiveBattle && Host.Renderer.VictoryAudioStarts == 1, "Cancelled re-entry left a battle or started a victory.");
        await RecordSilenceAfterTailAsync(Path.Combine(root, "after-cancel.wav"));
        ProbeRequirement.Require(Host.Renderer.ActiveAudioVoices == 0, "Physical channels survived the cancelled battle.");
    }

    /// <summary>Requires a physical note on the current track rather than only observing a restart counter.</summary>
    /// <returns>Completion after the track has armed a real mixer voice.</returns>
    private async Task WaitForPostBoundaryNotesAsync()
    {
        const double noteDeadlineSeconds = 2;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (Host.Renderer.ActiveAudioVoices == 0 && timer.Elapsed.TotalSeconds < noteDeadlineSeconds)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        ProbeRequirement.Require(Host.Renderer.ActiveAudioVoices > 0, "Music did not sustain a physical note.");
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
