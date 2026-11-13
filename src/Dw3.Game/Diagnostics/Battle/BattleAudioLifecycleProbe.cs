using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Records real entrance, input-driven change, host exit, re-entry and runner cancellation.</summary>
internal sealed class BattleAudioLifecycleProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleAudioLifecycleProbe"/> class.</summary>
    /// <param name="host">Actual offscreen GameHost adapter.</param>
    public BattleAudioLifecycleProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9061_LIFECYCLE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        ProbeRequirement.Require(BattleAudioRecording.Enabled, "Audio recording must be explicitly enabled.");
        Host.ApplyS200();
        string root = Host.PrepareCaptureDirectory("probe/battle-audio-lifecycle");
        await EnterAsync(root, "first-entrance");
        var renderer = Host.Renderer;
        await ReachPlayerAsync();
        await ChangeAsync(root);
        Host.ExitBattleForProbe();
        ProbeRequirement.Require(!Host.HasActiveBattle && !Host.State.BattleActive, "GameHost exit retained battle state.");
        await SilenceAsync(root, "after-exit");
        await EnterAsync(root, "second-entrance");
        ProbeRequirement.Require(ReferenceEquals(renderer, Host.Renderer), "Second battle replaced the renderer/backend.");
        await ReachPlayerAsync();
        await CancelAsync(root);
        Host.ExitBattleForProbe();
        await SilenceAsync(root, "after-final-exit");
        GD.Print("B9061_LIFECYCLE_PASS sameRenderer=true sameBackend=true exit=field secondCombat=started cancel=real-runner");
    }

    /// <summary>Requires a real recorder rather than a nullable disabled measurement.</summary>
    /// <param name="path">Private recording path.</param>
    /// <returns>An enabled physical-bus recorder.</returns>
    private static BattleAudioRecording StartRecording(string path) => BattleAudioRecording.Start(path)
        ?? throw new InvalidOperationException("Physical audio recording is disabled.");

    /// <summary>Records only the genuine host entrance, without starting unrelated enemy actions.</summary>
    /// <param name="root">Private proof directory.</param>
    /// <param name="name">Recording identity.</param>
    /// <returns>Completion after natural ADSR retirement.</returns>
    private async Task EnterAsync(string root, string name)
    {
        using var audio = StartRecording(Path.Combine(root, name + ".wav"));
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle, "Host battle setup failed.");
        await WaitSecondsAsync(7);
        audio.Save();
        ProbeRequirement.Require(Host.Renderer.ActiveAudioVoices == 0, "Entrance looping sample did not finish through its envelope.");
    }

    /// <summary>Confirms the real normal change through all three production menu pages.</summary>
    /// <param name="root">Private proof directory.</param>
    /// <returns>Completion after the incoming fighter's cue and natural release.</returns>
    private async Task ChangeAsync(string root)
    {
        int before = Host.Flow.State.ActivePlayerFighter?.Id ?? throw new InvalidOperationException("No outgoing fighter.");
        using var audio = StartRecording(Path.Combine(root, "change.wav"));
        InjectBattleKey(Key.S, 3);
        InjectBattleKey(Key.Enter, 4);
        int after = Host.Flow.State.ActivePlayerFighter?.Id ?? throw new InvalidOperationException("No incoming fighter.");
        ProbeRequirement.Require(after != before, "Normal input change did not select an incoming partner.");
        await WaitSecondsAsync(7);
        audio.Save();
        ProbeRequirement.Require(Host.Renderer.ActiveAudioVoices == 0, "Change retained an unending entrance note.");
        GD.Print($"B9061_CHANGE input=three-pages before={before} after={after}");
    }

    /// <summary>Waits for the real scheduler and host runner to yield the player's root turn.</summary>
    /// <returns>Completion at player command with drained trailing voices.</returns>
    private async Task ReachPlayerAsync()
    {
        foreach (var fighter in Host.Flow.State.Players.Concat(Host.Flow.State.Enemies).OfType<BattleFighter>())
        {
            fighter.Hp = fighter.MaxHp = 9000;
        }

        for (int frame = 0; frame < 1200 && Host.Flow.Phase != BattleFlowPhase.PlayerCommand; frame++)
        {
            await FramesAsync(1, true);
        }

        ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.PlayerCommand, "Normal host scheduler did not yield player command.");
        await WaitSecondsAsync(2);
    }

    /// <summary>Starts an actual host attack and cancels its retained runner while a real voice is active.</summary>
    /// <param name="root">Private proof directory.</param>
    /// <returns>Completion after a nonempty silent cleanup recording.</returns>
    private async Task CancelAsync(string root)
    {
        using (var audio = StartRecording(Path.Combine(root, "before-cancel.wav")))
        {
            InjectBattleKey(Key.Enter);
            ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.ActionSequence, "Attack did not start the host action runner.");
            for (int frame = 0; frame < 300 && (!audio.HasSignal || Host.Renderer.ActiveAudioVoices == 0); frame++)
            {
                await FramesAsync(1, true);
            }

            ProbeRequirement.Require(audio.HasSignal && Host.Renderer.ActiveAudioVoices > 0, "Cancellation never observed useful mixed output and an active physical voice.");
            Host.CancelSequenceForProbe();
            ProbeRequirement.Require(Host.Renderer.ActiveAudioVoices == 0, "Actual runner cancellation retained channels.");
            await FramesAsync(10, false);
            audio.Save();
        }

        await SilenceAsync(root, "after-cancel");
    }

    /// <summary>Requires a nonempty all-zero bus after the stopped mixer block drains.</summary>
    /// <param name="root">Private proof directory.</param>
    /// <param name="name">Recording identity.</param>
    /// <returns>Completion of the silent control.</returns>
    private async Task SilenceAsync(string root, string name)
    {
        await FramesAsync(15, false);
        using var audio = StartRecording(Path.Combine(root, name + ".wav"));
        await FramesAsync(20, false);
        audio.Save(false);
    }

    /// <summary>Waits for real mixer time without assuming that software-rendered frames run at 60 FPS.</summary>
    /// <param name="seconds">Minimum elapsed wall time.</param>
    /// <returns>Completion after real audio processing had the requested interval.</returns>
    private async Task WaitSecondsAsync(double seconds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < seconds)
        {
            await Host.NextFrameDrawn();
        }
    }

    /// <summary>Advances real offscreen frames, optionally through the host simulation entry.</summary>
    /// <param name="count">Frame count, not a source-time parity claim.</param>
    /// <param name="simulate">Whether normal host gameplay should advance.</param>
    /// <returns>Completion after all requested engine frames.</returns>
    private async Task FramesAsync(int count, bool simulate)
    {
        for (int frame = 0; frame < count; frame++)
        {
            if (simulate)
            {
                Host.TickTestSimulation();
            }

            await Host.NextFrameDrawn();
        }
    }
}
