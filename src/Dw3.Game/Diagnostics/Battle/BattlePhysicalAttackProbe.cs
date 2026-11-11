using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises real basic-attack scripts through production rules, renderer and runner offscreen.</summary>
internal sealed class BattlePhysicalAttackProbe : BattleProbe
{
    private BattleFighter? _reserve;

    /// <summary>Initializes a new instance of the <see cref="BattlePhysicalAttackProbe"/> class.</summary>
    /// <param name="host">Restricted offscreen host.</param>
    public BattlePhysicalAttackProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9052";

    /// <summary>Gets the active player required by this controlled setup.</summary>
    private BattleFighter Player => Host.Flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("No player.");

    /// <summary>Gets the active enemy required by this controlled setup.</summary>
    private BattleFighter Enemy => Host.Flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("No enemy.");

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.ApplyS200();
        using var loader = new BattleDataPackLoader(new DataPackReader(System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit proof pack is required.")));
        foreach (string mode in new[] { "normal", "miss", "fatal-reserve" })
        {
            Host.StartDebugBattle();
            await Host.NextFrameDrawn();
            Host.Renderer.ConfigureActionAssets(loader);
            ConfigureControls();
            VerifyJumpControls();
            VerifyMotionCleanup();
            var enemy = Enemy;
            enemy.Hp = enemy.MaxHp = 9000;
            Player.Hp = 9000;
            Player.MaxHp = 9000;
            enemy.Counter = default;
            enemy.Actions = [new BattleTableAction(1, 0, 0)];
            var captures = Host.PrepareCaptureDirectory($"probe/battle-physical/{mode}");
            await BattleProbeCapture.CaptureAsync(Host, captures, "before.png");
            await RunPlayerAsync(loader, mode, captures);
            ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.EnemyTurn, $"enemy turn unavailable {Host.Flow.Phase}");
            await RunEnemyAsync(loader, mode, captures);
            if (mode == "normal")
            {
                for (int repetition = 0; repetition < 4 && Host.Flow.Phase == BattleFlowPhase.PlayerCommand; repetition++)
                {
                    await RunPlayerAsync(loader, $"repeat-{repetition}", captures);
                }

                ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.EnemyTurn, "repeated player actions did not yield an enemy turn");
                await RunEnemyAsync(loader, "repeat", captures);
            }
        }

        await RunFinalCasesAsync(loader);
        GD.Print("B9052_PASS two-camps normal/miss/fatal-reserve/repeat physical-only audio-control-not-played");
    }

    /// <summary>Requires the requested resolved branch rather than assuming an input produced it.</summary>
    /// <param name="mode">Requested branch.</param>
    /// <param name="flow">Resolved flow.</param>
    /// <param name="target">Original physical target.</param>
    private static void VerifyBranch(string mode, BattleFlowController flow, BattleFighter target)
    {
        var hit = flow.State.LastHitEvents.First(hit => !hit.IsCounter);
        ProbeRequirement.Require(hit.Landed == (mode != "miss"), $"wrong landed branch {mode}: {hit}");
        ProbeRequirement.Require(target.IsKnockedOut == mode.StartsWith("fatal", StringComparison.Ordinal), $"wrong KO branch {mode}: {hit}");
        GD.Print($"B9052_BRANCH {mode} hit={hit} target-hp={target.Hp} phase={flow.Phase}");
    }

    /// <summary>Installs a named sound-only control without faking graphics or reactions.</summary>
    private void ConfigureControls()
    {
        Host.Renderer.SequencePlayback.AssetConsumer = request =>
        {
            GD.Print($"B9052_CONTROL_ACK_NOT_RENDERED {request}");
            return request is SoundRequestEvent ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable;
        };
    }

    /// <summary>Resolves one player action with controlled branch inputs and the real rule implementation.</summary>
    /// <param name="loader">Real extracted pack.</param>
    /// <param name="mode">Branch setup.</param>
    /// <param name="captures">Capture directory.</param>
    /// <returns>Completion after physical return.</returns>
    private async Task RunPlayerAsync(BattleDataPackLoader loader, string mode, string captures)
    {
        var flow = Host.Flow;
        var target = Enemy;
        if (mode == "fatal-reserve")
        {
            var reserve = _reserve ?? throw new InvalidOperationException("Missing real reserve fixture.");
            flow.State.Enemies[1] = reserve;
            reserve.Hp = 9000;
            target.Hp = 1;
        }

        if (mode == "fatal-final")
        {
            target.Hp = 1;
        }

        int accuracy = flow.PlayerBasicTech.Accuracy;
        flow.PlayerBasicTech.Accuracy = mode == "miss" ? 1 : accuracy;
        var rng = new BattleAttackProofRng(mode == "miss" ? 127 : 0);
        ProbeRequirement.Require(flow.Choose(BattleCommand.Attack, 0, rng), "player attack refused");
        flow.PlayerBasicTech.Accuracy = accuracy;
        await PlayAsync(loader, rng, mode + "-player", captures);
        VerifyBranch(mode, flow, target);
    }

    /// <summary>Resolves the current enemy's source basic attack without replaying the player's rules.</summary>
    /// <param name="loader">Real extracted pack.</param>
    /// <param name="mode">Branch setup.</param>
    /// <param name="captures">Capture directory.</param>
    /// <returns>Completion after physical return.</returns>
    private async Task RunEnemyAsync(BattleDataPackLoader loader, string mode, string captures)
    {
        var flow = Host.Flow;
        var target = Player;
        if (mode.StartsWith("fatal", StringComparison.Ordinal))
        {
            target.Hp = 1;
        }

        var rng = new BattleAttackProofRng(mode == "miss" ? 127 : 0);
        flow.EnemyAct(rng);
        await PlayAsync(loader, rng, mode + "-enemy", captures);
        VerifyBranch(mode, flow, target);
        _reserve = flow.State.ActiveEnemyFighter;
    }

    /// <summary>Exercises final victory and defeat without allowing either to bypass the physical action.</summary>
    /// <param name="loader">Real extracted action pack.</param>
    /// <returns>Completion after both final actions reach End.</returns>
    private async Task RunFinalCasesAsync(BattleDataPackLoader loader)
    {
        var captures = Host.PrepareCaptureDirectory("probe/battle-physical/final");
        Host.StartDebugBattle();
        await Host.NextFrameDrawn();
        Host.Renderer.ConfigureActionAssets(loader);
        ConfigureControls();
        await BattleProbeCapture.CaptureAsync(Host, captures, "before-victory.png");
        await RunPlayerAsync(loader, "fatal-final", captures);
        ProbeRequirement.Require(Host.Flow.Outcome == BattleOutcome.Won, "final player attack did not produce victory");
        Host.StartDebugBattle();
        await Host.NextFrameDrawn();
        Host.Renderer.ConfigureActionAssets(loader);
        ConfigureControls();
        Enemy.Hp = Enemy.MaxHp = 9000;
        Enemy.Counter = default;
        Enemy.Actions = [new BattleTableAction(1, 0, 0)];
        await BattleProbeCapture.CaptureAsync(Host, captures, "before-defeat.png");
        await RunPlayerAsync(loader, "normal", captures);
        foreach (var reserve in Host.Flow.State.Players.Skip(1).OfType<BattleFighter>())
        {
            reserve.Hp = 0;
        }

        await RunEnemyAsync(loader, "fatal-final", captures);
        ProbeRequirement.Require(Host.Flow.Outcome == BattleOutcome.Lost, "final enemy attack did not produce defeat");
        GD.Print("B9052_FINAL victory-and-defeat-after-physical-End");
    }

    /// <summary>Exercises the real loader, timeline, scene, contact children and restoration without RNG draws.</summary>
    /// <param name="loader">Extracted pack loader.</param>
    /// <param name="rng">Simulation stream whose count must remain frozen.</param>
    /// <param name="label">Trace identity.</param>
    /// <param name="captures">PNG output directory.</param>
    /// <returns>Completion after End returns control.</returns>
    private async Task PlayAsync(BattleDataPackLoader loader, BattleAttackProofRng rng, string label, string captures)
    {
        var flow = Host.Flow;
        ProbeRequirement.Require(flow.Phase == BattleFlowPhase.ActionSequence, "action has no real script");
        var context = flow.PendingSequenceContext ?? throw new InvalidOperationException("No pending context.");
        GD.Print($"B9052_BEGIN {label} fighter={context.AttackerId} selector={context.ScriptSelector} target={context.TargetId} stage={context.ScriptStage} hits={System.Text.Json.JsonSerializer.Serialize(context.HitEvents)}");
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetRenderer(Host.Renderer);
        int draws = rng.Count;
        int contacts = 0;
        var roster = Host.Renderer.SequencePlayback.DiagnosticRoster;
        var homes = roster.Models.ToDictionary(model => model, model => model.Root.Position);
        var camera = Host.Renderer.CameraForProbe.Transform;
        for (int tick = 0; tick < 1200 && flow.Phase == BattleFlowPhase.ActionSequence; tick++)
        {
            runner.Tick();
            Host.Renderer.Present(Host.BuildBattleView());
            ProbeRequirement.Require(runner.Failure is null, $"{label} tick={tick}: {runner.Failure}");
            if (tick % 20 == 0)
            {
                GD.Print($"B9052_TICK {label} tick={tick} cursor={runner.DiagnosticCursor} player={roster.Player?.Root.Position} enemy={roster.Enemy?.Root.Position}");
            }

            await Host.NextFrameDrawn();
            int drawnContacts = Host.Renderer.SequencePlayback.DiagnosticImpactCount;
            if (drawnContacts > contacts)
            {
                contacts = drawnContacts;
                await BattleProbeCapture.CaptureRawAsync(Host, captures, $"{label}-contact-{contacts}.png");
                GD.Print($"B9052_CONTACT {label} tick={tick} actual-children={contacts}");
            }

            if (tick is 25 or 93 or 97 or 110 or 150 or 210)
            {
                await BattleProbeCapture.CaptureRawAsync(Host, captures, $"{label}-{tick}.png");
            }
        }

        ProbeRequirement.Require(contacts > 0, "no real contact child was rendered");
        ProbeRequirement.Require(flow.Phase != BattleFlowPhase.ActionSequence, $"{label} sequence stalled");
        ProbeRequirement.Require(rng.Count == draws, "playback redrew simulation RNG");
        ProbeRequirement.Require(homes.All(pair => pair.Key.Root.Position.IsEqualApprox(pair.Value)), "positions not restored");
        ProbeRequirement.Require(Host.Renderer.CameraForProbe.Transform.IsEqualApprox(camera), "camera not restored");
        await BattleProbeCapture.CaptureRawAsync(Host, captures, $"{label}-returned.png");
        GD.Print($"B9052_RETURN {label} phase={flow.Phase} draws={draws}/{rng.Count} restore=exact captures={captures}");
    }

    /// <summary>Requires cancellation to restore the prior idle selection without overwriting normal authored endings.</summary>
    private void VerifyMotionCleanup()
    {
        var consumer = Host.Renderer.SequencePlayback;
        var model = consumer.DiagnosticRoster.Player ?? throw new InvalidOperationException("No player.");
        var context = new ActionSequenceContext(BattleSide.Player, 383, 51, [], 5);
        model.SetMotion(2, true);
        consumer.Begin(context);
        model.SetMotion(16, true);
        consumer.Finish(true);
        ProbeRequirement.Require(model.Motion?.Motion == 2, "cancel did not restore the original idle motion");
        consumer.Begin(context);
        model.SetMotion(16, true);
        consumer.Finish(false);
        ProbeRequirement.Require(model.Motion?.Motion == 16, "normal End overwrote the authored reaction pose");
        model.SetMotion(1, true);
        GD.Print("B9052_ASSERT cancel-motion=restored normal-motion=preserved");
    }

    /// <summary>Executes the independently suspected wait-motion and cancellation regressions on live models.</summary>
    private void VerifyJumpControls()
    {
        var consumer = Host.Renderer.SequencePlayback;
        var model = consumer.DiagnosticRoster.Player ?? throw new InvalidOperationException("No rendered player.");
        var home = model.Root.Position;
        var context = new ActionSequenceContext(BattleSide.Player, 383, 51, [], 5);
        consumer.Begin(context);
        consumer.Consume(new ModelJumpEvent(BattleSide.Player, 0, 4, -384));
        for (int i = 0; i < 64; i++)
        {
            consumer.StepPhysical();
        }

        consumer.Consume(new ModelMotionEvent(BattleSide.Player, 0, 0, 15));
        ProbeRequirement.Require(!consumer.MotionReady(0), "completed jump falsely released the new motion");
        consumer.Finish(true);
        consumer.Begin(context);
        consumer.Consume(new ModelJumpEvent(BattleSide.Player, 0, 4, -384));
        consumer.StepPhysical();
        ProbeRequirement.Require(!home.IsEqualApprox(model.Root.Position), "cancellation control did not move");
        consumer.Finish(true);
        consumer.Begin(context);
        ProbeRequirement.Require(home.IsEqualApprox(model.Root.Position), "cancelled jump poisoned real re-entry");
        consumer.Finish(true);
        GD.Print("B9052_ASSERT motion-after-jump=False cancellation-reentry=exact");
    }
}
