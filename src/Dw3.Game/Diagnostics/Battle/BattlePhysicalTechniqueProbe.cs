using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises extracted physical techniques through the real renderer and retained runner.</summary>
internal sealed class BattlePhysicalTechniqueProbe : BattleProbe
{
    private static readonly int[] Fighters = [388, 19, 392];
    private static readonly string[] Modes = ["normal", "miss", "fatal-reserve", "fatal-final"];

    /// <summary>Initializes a new instance of the <see cref="BattlePhysicalTechniqueProbe"/> class.</summary>
    /// <param name="host">Restricted offscreen host.</param>
    public BattlePhysicalTechniqueProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9053";

    /// <summary>Gets the selected technique for bounded private composition batches.</summary>
    private static int TechniqueId => int.Parse(System.Environment.GetEnvironmentVariable("DW3_PHYSICAL_TECHNIQUE") ?? "123", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Gets one explicitly selected natural owner or the original retained first lot.</summary>
    private static IEnumerable<int> CandidateFighters => System.Environment.GetEnvironmentVariable("DW3_PHYSICAL_FIGHTER") is { Length: > 0 } fighter
        ? [int.Parse(fighter, System.Globalization.CultureInfo.InvariantCulture)] : Fighters;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.ApplyS200();
        using var loader = new BattleDataPackLoader(new DataPackReader(System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit pack required.")));
        string root = Host.PrepareCaptureDirectory("probe/battle-technique");
        BattlePhysicalRegistry.Write(loader, root);
        foreach (int fighter in CandidateFighters.Where(fighter => fighter != 178 || TechniqueId != 123))
        {
            foreach (string mode in Modes.Where(mode => Selected("DW3_PHYSICAL_MODE", mode)))
            {
                if (System.Environment.GetEnvironmentVariable("DW3_PHYSICAL_ENEMY") == "1")
                {
                    await BattlePhysicalEnemyTechniqueCase.RunAsync(Host, loader, root, mode, (rng, captures) => PlayAsync(loader, rng, captures), (fighter, TechniqueId));
                }
                else
                {
                    await RunCaseAsync(loader, root, fighter, mode);
                }
            }
        }

        if (TechniqueId == 123 && Selected("DW3_PHYSICAL_FIGHTER", "178"))
        {
            foreach (string mode in Modes.Where(mode => Selected("DW3_PHYSICAL_MODE", mode)))
            {
                await BattlePhysicalEnemyTechniqueCase.RunAsync(Host, loader, root, mode, (rng, captures) => PlayAsync(loader, rng, captures));
            }
        }

        GD.Print("B9053_PASS real-techniques audio-control-not-played");
    }

    /// <summary>Filters private cases to keep offscreen execution batches bounded.</summary>
    /// <param name="key">Optional filter variable.</param>
    /// <param name="value">Case identity.</param>
    /// <returns>Whether this case was selected.</returns>
    private static bool Selected(string key, string value) => System.Environment.GetEnvironmentVariable(key) is not { Length: > 0 } filter || filter == value;

    /// <summary>Sets controlled branch inputs without replacing the technique's animation or rules.</summary>
    /// <param name="loader">Validated pack.</param>
    /// <param name="root">Private output root.</param>
    /// <param name="fighter">Natural skill owner.</param>
    /// <param name="mode">Controlled outcome branch.</param>
    /// <returns>Completion after the selected branch and repetition.</returns>
    private async Task RunCaseAsync(BattleDataPackLoader loader, string root, int fighter, string mode)
    {
        Host.StartDebugBattle();
        await Host.NextFrameDrawn();
        var flow = Host.Flow;
        var player = flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("No player.");
        var target = flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("No enemy.");
        player.Id = fighter;
        player.Mp = player.MaxMp = 9000;
        player.Hp = player.MaxHp = 9000;
        target.Hp = target.MaxHp = mode.StartsWith("fatal", StringComparison.Ordinal) ? 1 : 9000;
        target.Counter = default;
        if (mode == "fatal-reserve")
        {
            flow.State.Enemies[1] = new BattleFighter { Id = 32, Hp = 9000, MaxHp = 9000 };
        }

        Host.Renderer.BuildModels(loader.ModelPath(player.Id), loader.ModelPath(target.Id), "physical proof", "target", target.Id);
        Host.AdoptSheetFighter(player.Id);
        Host.Renderer.ConfigureActionAssets(loader);
        Host.Renderer.SequencePlayback.AssetConsumer = request => request is SoundRequestEvent ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable;
        var technique = BattleSetupFactory.ToTech(loader.LoadTechs().Techs[TechniqueId]);
        technique.Accuracy = mode == "miss" ? 1 : technique.Accuracy;
        flow.PlayerTechniques = [new BattleTechnique(TechniqueId, "extracted", technique)];
        string captures = Path.Combine(root, $"{fighter}-{mode}");
        Directory.CreateDirectory(captures);
        await BattleProbeCapture.CaptureAsync(Host, captures, "before.png");
        var rng = new BattleAttackProofRng(mode == "miss" ? 127 : 0);
        ProbeRequirement.Require(flow.Choose(BattleCommand.Technique, TechniqueId, rng), "technique refused");
        ProbeRequirement.Require(flow.State.LastHitEvents.All(hit => hit.Landed == (mode != "miss")), "wrong branch");
        await PlayAsync(loader, rng, captures);
        ProbeRequirement.Require(player.Mp == 9000 - technique.Mp, "technique debited more than once");
        ProbeRequirement.Require(target.IsKnockedOut == mode.StartsWith("fatal", StringComparison.Ordinal), "wrong KO branch");
        if (mode == "normal")
        {
            await RepeatAsync(loader, captures, rng);
        }
    }

    /// <summary>Reuses the same battle and renderer for another complete action after the real enemy reply.</summary>
    /// <param name="loader">Validated pack.</param>
    /// <param name="captures">Case directory.</param>
    /// <param name="rng">Retained controlled stream.</param>
    /// <returns>Completion after repeated technique playback.</returns>
    private async Task RepeatAsync(BattleDataPackLoader loader, string captures, BattleAttackProofRng rng)
    {
        var flow = Host.Flow;
        var enemy = flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("No enemy.");
        enemy.Actions = [new BattleTableAction(1, 0, 0)];
        flow.EnemyAct(rng);
        await PlayAsync(loader, rng, captures + "/reply");
        ProbeRequirement.Require(flow.Choose(BattleCommand.Technique, TechniqueId, rng), "repeat refused");
        await PlayAsync(loader, rng, captures + "/repeat");
    }

    /// <summary>Runs to physical return and verifies actual per-hit consumers, coordinates and frozen state.</summary>
    /// <param name="loader">Validated real pack.</param>
    /// <param name="rng">Resolved simulation stream.</param>
    /// <param name="captures">Private capture directory.</param>
    /// <returns>Completion after the full action.</returns>
    private async Task PlayAsync(BattleDataPackLoader loader, BattleAttackProofRng rng, string captures)
    {
        Directory.CreateDirectory(captures);
        var flow = Host.Flow;
        var context = flow.PendingSequenceContext ?? throw new InvalidOperationException("No sequence.");
        var provider = new PackActionScriptProvider(loader);
        var script = provider.TryLoad(context.AttackerId, context.ScriptSelector) ?? throw new InvalidOperationException("No real script.");
        GD.Print($"B9053_SOURCE {captures} {System.Text.Json.JsonSerializer.Serialize(script)}");
        GD.Print($"B9053_BEGIN {System.Text.Json.JsonSerializer.Serialize(context)}");
        var runner = new BattleSequenceRunner(flow, provider);
        runner.SetRenderer(Host.Renderer);
        Host.Renderer.SetOpeningCamera();
        var audit = new BattlePhysicalTechniqueAudit(Host, context, script);
        int draws = rng.Count;
        int contacts = 0;
        for (int tick = 0; tick < 1800 && flow.Phase == BattleFlowPhase.ActionSequence; tick++)
        {
            runner.Tick();
            Host.Renderer.Present(Host.BuildBattleView());
            ProbeRequirement.Require(runner.Failure is null, $"tick={tick} cursor={runner.DiagnosticCursor}: {runner.Failure}");
            await Host.NextFrameDrawn();
            int count = Host.Renderer.SequencePlayback.DiagnosticImpactCount;
            if (count > contacts)
            {
                contacts = count;
                await BattleProbeCapture.CaptureRawAsync(Host, captures, $"contact-{count}.png");
                GD.Print($"B9053_CONTACT tick={tick} count={count}");
            }
        }

        audit.Verify();
        ProbeRequirement.Require(flow.Phase != BattleFlowPhase.ActionSequence, "sequence stalled");
        ProbeRequirement.Require(rng.Count == draws, "playback redrew RNG");
        await BattleProbeCapture.CaptureRawAsync(Host, captures, "returned.png");
        GD.Print($"B9053_RETURN case={captures} phase={flow.Phase} rng={draws}/{rng.Count} contacts={contacts}");
    }
}
