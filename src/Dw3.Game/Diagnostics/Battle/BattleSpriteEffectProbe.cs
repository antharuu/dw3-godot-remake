using System.Diagnostics;
using System.Globalization;
using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Plays extracted techniques whose scripts start prepared sprite effects and a white flash through the
/// production runner and renderer, offscreen, then cancels a second run while its sprites are drawn.
/// </summary>
internal sealed class BattleSpriteEffectProbe : BattleProbe
{
    /// <summary>Default cases as technique:fighter, an <c>e</c> prefix running the natural enemy owner.</summary>
    private const string DefaultCases = "e258:273,70:260,e79:453";

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteEffectProbe"/> class.</summary>
    /// <param name="host">Restricted offscreen host.</param>
    public BattleSpriteEffectProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9056";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.ApplyS200();
        using var loader = new BattleDataPackLoader(new DataPackReader(System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit pack required.")));
        string root = Host.PrepareCaptureDirectory("probe/battle-sprites");
        var clock = Stopwatch.StartNew();
        BattleSpriteCatalog catalog = loader.LoadSpriteCatalog();
        GD.Print($"B9056_CATALOG ids={catalog.Index.Count} validate_ms={clock.ElapsedMilliseconds}");
        BattleSpriteRegistryRecount.Write(catalog, Path.Combine(root, "sprite-registry.json"));
        foreach ((bool enemy, int technique, int fighter) in Cases())
        {
            foreach (bool cancel in new[] { false, true })
            {
                await RunCaseAsync(loader, Path.Combine(root, $"{(enemy ? "enemy-" : string.Empty)}{technique}-{fighter}-{(cancel ? "cancel" : "normal")}"), (enemy, technique, fighter), cancel);
            }
        }

        GD.Print("B9056_PASS sprites flash numbers digits lifecycle");
    }

    /// <summary>Reads the selected technique and fighter pairs.</summary>
    /// <returns>Owner side, technique and natural owner.</returns>
    private static IEnumerable<(bool Enemy, int Technique, int Fighter)> Cases() =>
        (System.Environment.GetEnvironmentVariable("DW3_SPRITE_CASES") ?? DefaultCases)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(':'))
            .Select(pair => (pair[0].StartsWith('e'), int.Parse(pair[0].TrimStart('e'), CultureInfo.InvariantCulture), int.Parse(pair[1], CultureInfo.InvariantCulture)));

    /// <summary>Requires drawn pixels for the started sprites and a saturating flash when one reached full white.</summary>
    /// <param name="evidence">Completed normal run.</param>
    private static void Require(BattleSpriteEffectEvidence evidence)
    {
        ProbeRequirement.Require(evidence.Seen.Count > 0, "no sprite child was created");
        ProbeRequirement.Require(evidence.BestPixels.Values.Max() > 0, "sprites drew no pixel");
        ProbeRequirement.Require(evidence.FlashPeak is not { } flash || flash.White * 10 >= flash.Total * 9, "full white flash did not saturate the frame");
    }

    /// <summary>Starts a debug battle with the natural owner and resolves the technique through the real rules.</summary>
    /// <param name="loader">Validated pack.</param>
    /// <param name="captures">Private case directory.</param>
    /// <param name="selected">Owner side, technique and natural owner.</param>
    /// <param name="cancel">Whether to cancel the action while sprites are drawn.</param>
    /// <returns>Completion after verification.</returns>
    private async Task RunCaseAsync(BattleDataPackLoader loader, string captures, (bool Enemy, int Technique, int Fighter) selected, bool cancel)
    {
        Host.StartDebugBattle();
        await Host.NextFrameDrawn();
        var flow = Host.Flow;
        var player = flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("No player.");
        var target = flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("No enemy.");
        player.Id = selected.Enemy ? player.Id : selected.Fighter;
        target.Id = selected.Enemy ? selected.Fighter : 51;
        player.Mp = player.MaxMp = 9000;
        player.Hp = player.MaxHp = 9000;
        target.Hp = target.MaxHp = 9000;
        target.Counter = default;
        Host.Renderer.BuildModels(loader.ModelPath(player.Id), loader.ModelPath(target.Id), "sprite proof", "target", target.Id);
        Host.AdoptSheetFighter(player.Id);
        Host.Renderer.ConfigureActionAssets(loader);
        Host.Renderer.SequencePlayback.AssetConsumer = request => request is SoundRequestEvent ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable;
        await ReachPlayerAsync();
        Directory.CreateDirectory(captures);
        await BattleProbeCapture.CaptureAsync(Host, captures, "before.png");
        var tech = BattleSetupFactory.ToTech(loader.LoadTechs().Techs[selected.Technique]);
        var rng = new BattleAttackProofRng(0);
        if (selected.Enemy)
        {
            await ResolveEnemyAsync(loader, captures, target, (selected.Technique, tech), rng);
        }
        else
        {
            flow.PlayerTechniques = [new BattleTechnique(selected.Technique, "extracted", tech)];
        }

        Image settled = await BattleSpriteEffectEvidence.SettleAsync(Host);
        settled.SavePng(Path.Combine(captures, "hud-digits-before.png"));
        var before = (Image: settled, PlayerHp: player.Hp);
        BattleFighter defender = selected.Enemy ? player : target;
        int defenderHp = defender.Hp;
        if (selected.Enemy)
        {
            flow.EnemyAct(rng);
        }
        else
        {
            ProbeRequirement.Require(flow.Choose(BattleCommand.Technique, selected.Technique, rng), $"technique refused phase={flow.Phase}");
        }

        ProbeRequirement.Require(flow.PendingSequenceContext is { } context && context.AttackerId == selected.Fighter && context.ScriptSelector == tech.Script, "wrong technique context");
        await PlayCaseAsync(loader, captures, (cancel, (flow.PendingSequenceContext!, defender, defenderHp)), before);
    }

    /// <summary>Plays a real opening attack, then gives the natural enemy owner its technique slot.</summary>
    /// <param name="loader">Validated pack.</param>
    /// <param name="captures">Private case directory.</param>
    /// <param name="enemy">Enemy fighter.</param>
    /// <param name="technique">Technique id and resolved data.</param>
    /// <param name="rng">Controlled stream.</param>
    /// <returns>Completion when the enemy turn is ready.</returns>
    private async Task ResolveEnemyAsync(BattleDataPackLoader loader, string captures, BattleFighter enemy, (int Id, Dw3Production.Simulation.TechData Data) technique, BattleAttackProofRng rng)
    {
        var flow = Host.Flow;
        enemy.Techs = [.. loader.LoadFighters().BattleTable.Single(entry => entry.Id == enemy.Id).Techs];
        int slot = Array.IndexOf(enemy.Techs, technique.Id);
        ProbeRequirement.Require(slot >= 0, "technique has no natural enemy slot");
        enemy.Actions = [new BattleTableAction(slot + 1, 0, 0)];
        int enemyHp = enemy.Hp;
        ProbeRequirement.Require(flow.Choose(BattleCommand.Attack, 0, rng), $"opening attack refused phase={flow.Phase}");
        await PlayCaseAsync(loader, Path.Combine(captures, "opening"), (false, (flow.PendingSequenceContext!, enemy, enemyHp)), before: null);
        ProbeRequirement.Require(flow.Phase == BattleFlowPhase.EnemyTurn, $"enemy turn unavailable phase={flow.Phase}");
        if (slot == 0)
        {
            flow.EnemyBasicTech = technique.Data;
        }

        flow.Techs = new Dictionary<int, Dw3Production.Simulation.TechData> { [technique.Id] = technique.Data };
        flow.State.Techs = flow.Techs;
    }

    /// <summary>Plays one resolved action and verifies its children, flash, pixels, numbers, digits and destruction.</summary>
    /// <param name="loader">Validated pack.</param>
    /// <param name="captures">Private case directory.</param>
    /// <param name="action">Whether to cancel once sprites drew, and the frozen rule result with its defender and the defender's HP before resolution.</param>
    /// <param name="before">Settled capture and active player HP before the action was resolved, or null when digits are not checked.</param>
    /// <returns>Completion after verification.</returns>
    private async Task PlayCaseAsync(BattleDataPackLoader loader, string captures, (bool Cancel, (ActionSequenceContext Context, BattleFighter Defender, int Hp) Rule) action, (Image Image, int PlayerHp)? before)
    {
        bool cancel = action.Cancel;
        var evidence = new BattleSpriteEffectEvidence(Host, captures);
        var numbers = new BattleActionNumberEvidence(Host, captures);
        var runner = new BattleSequenceRunner(Host.Flow, new PackActionScriptProvider(loader));
        runner.SetRenderer(Host.Renderer);
        Host.Renderer.SetOpeningCamera();
        var state = Host.Flow.State.Players.Concat(Host.Flow.State.Enemies).OfType<BattleFighter>().Select(fighter => (fighter, fighter.Hp)).ToArray();
        bool cancelled = await PlayAsync(runner, (evidence, numbers), cancel);
        await evidence.VerifyReleasedAsync(cancelled ? "cancelled" : "normal");
        ProbeRequirement.Require(cancel == cancelled, "cancellation point never reached");
        ProbeRequirement.Require(state.All(item => item.fighter.Hp == item.Hp), "playback changed rules state");
        ProbeRequirement.Require(!cancel || (numbers.Page is null && Host.Flow.ActionResult is null), "a cancelled action showed its result message");
        if (!cancel)
        {
            Require(evidence);
            numbers.RequireRuleResult(action.Rule.Context, action.Rule.Hp, action.Rule.Defender.Hp);
        }

        if (!cancel && before is { } previous)
        {
            int changed = await evidence.CaptureHudDigitsAsync(previous.Image);
            bool hpChanged = Host.Flow.State.ActivePlayerFighter?.Hp != previous.PlayerHp;
            ProbeRequirement.Require(!hpChanged || changed > 0, "player HP changed but the HUD digits did not");
            GD.Print($"B9056_HP before={previous.PlayerHp} after={Host.Flow.State.ActivePlayerFighter?.Hp} digits_changed={changed} hp_changed={hpChanged}");
        }

        await BattleProbeCapture.CaptureRawAsync(Host, captures, "returned.png");
        GD.Print($"B9056_CASE {captures} mode={(cancel ? "cancel" : "normal")} children={evidence.Seen.Count} pixels={string.Join(' ', evidence.BestPixels.Select(pair => $"{pair.Key}:{pair.Value}"))} flash_white={evidence.FlashPeak?.White}/{evidence.FlashPeak?.Total} digits={evidence.DigitPixels}");
    }

    /// <summary>Advances the production runner and renderer, observing every frame.</summary>
    /// <param name="runner">Production runner.</param>
    /// <param name="observers">Sprite and result-number frame observers.</param>
    /// <param name="cancel">Whether to cancel once a sprite drew pixels.</param>
    /// <returns>Whether the action was cancelled.</returns>
    private async Task<bool> PlayAsync(BattleSequenceRunner runner, (BattleSpriteEffectEvidence Sprites, BattleActionNumberEvidence Numbers) observers, bool cancel)
    {
        BattleSpriteEffectEvidence evidence = observers.Sprites;
        for (int tick = 0; tick < 2400 && Host.Flow.Phase == BattleFlowPhase.ActionSequence; tick++)
        {
            runner.Tick();
            Host.Renderer.Present(Host.BuildBattleView());
            ProbeRequirement.Require(runner.Failure is null, $"tick={tick} cursor={runner.DiagnosticCursor}: {runner.Failure}");
            await Host.NextFrameDrawn();
            await evidence.ObserveAsync(tick);
            await observers.Numbers.ObserveAsync(tick);
            if (cancel && evidence.BestPixels.Values.Any(pixels => pixels > 0))
            {
                runner.Cancel();
                GD.Print($"B9056_CANCEL tick={tick} cursor={runner.DiagnosticCursor}");
                return true;
            }
        }

        ProbeRequirement.Require(Host.Flow.Phase != BattleFlowPhase.ActionSequence, "sequence stalled");
        return false;
    }

    /// <summary>
    /// Consumes a genuine enemy opening before resolving the controlled technique. The probe owns the
    /// driver, so it presents each frame itself: the host runner's physical children only advance there.
    /// </summary>
    /// <returns>Completion when the normal scheduler yields player command.</returns>
    private async Task ReachPlayerAsync()
    {
        for (int frame = 0; frame < 2400 && Host.Flow.Phase != BattleFlowPhase.PlayerCommand; frame++)
        {
            Host.TickTestSimulation();
            Host.Renderer.Present(Host.BuildBattleView());
            await Host.NextFrameDrawn();
        }

        ProbeRequirement.Require(Host.Flow.Phase == BattleFlowPhase.PlayerCommand, $"opening did not yield player command: {Host.Flow.Phase}");
    }
}
