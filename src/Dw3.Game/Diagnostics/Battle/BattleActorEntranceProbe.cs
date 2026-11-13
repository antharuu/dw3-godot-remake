using System.Globalization;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Proves the source entrance callers on the production frame coordinator, renderer and physical steps:
/// an enemy's voluntary switch (encounter 4, whose fighter 222 always switches to 77) enters with the normal idle even
/// at a quarter of its HP, the switched-out 222 replacing the knocked-out 77 after a real attack enters with the weak
/// idle chosen by its HP, both followed by the command turn without a command-camera overwrite of their caller's message, then the
/// last boss's fighters 0x1D2 (stage 0x16, ten-unit hold) and 0x1D3 (music change) of encounter 324.
/// </summary>
internal sealed class BattleActorEntranceProbe : BattleProbe
{
    /// <summary>Encounter whose first enemy always switches to its second.</summary>
    private const int SwitchEncounter = 4;

    /// <summary>Last boss encounter, enemies 442, 0x1D2 and 0x1D3.</summary>
    private const int LastBossEncounter = 324;

    /// <summary>Post-message units before an enemy entrance's scripted Cross.</summary>
    private const int EnemyConfirmAfter = 20;

    /// <summary>Optional variable running only one half of the probe, so each half fits the offscreen budget.</summary>
    private const string ScopeVariable = "DW3_ACTOR_ENTRANCE_SCOPE";

    /// <summary>Scope value running only the encounter 4 switch and replacement.</summary>
    private const string SwitchScope = "switch";

    /// <summary>Scope value running only the last boss entrances.</summary>
    private const string LastBossScope = "lastboss";

    /// <summary>Party HP of the last boss fixture, high enough that no partner falls before the replacements.</summary>
    private const int LastBossFixtureHp = 500_000;

    /// <summary>Units the voluntary switch's name message, opened at the swap, stays open: past the entrance's end, so
    /// the proof also covers the enemy view held under that message.</summary>
    private const int EnemySwitchConfirmAfter = 240;

    /// <summary>Initializes a new instance of the <see cref="BattleActorEntranceProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleActorEntranceProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_ACTOR_ENTRANCE_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        string root = Host.PrepareCaptureDirectory("probe/battle-actor-entrance");
        Host.ApplyS200();
        var driver = new BattleProductionFrameDriver(Host.FrameHost);
        var proof = new BattleEntranceCallProof(Host, driver, root);
        string scope = System.Environment.GetEnvironmentVariable(ScopeVariable) ?? string.Empty;
        if (scope != LastBossScope)
        {
            await EnemySwitchAndReplacementAsync(driver, proof, root);
            Host.ExitBattleForProbe();
        }

        if (scope != SwitchScope)
        {
            await LastBossEntrancesAsync(driver, proof);
        }

        QuietLog.Info(string.Create(CultureInfo.InvariantCulture, $"battle-actor-entrance probe PASS scope={(scope.Length == 0 ? "all" : scope)} ticks={driver.Ticks} fed={driver.Seconds:0.000}s frames={driver.Frames} dropped={driver.DroppedTicks} captures={root}"));
    }

    /// <summary>Gives a fighter a large nonterminal HP pool, as the other battle probes' fixtures do.</summary>
    /// <param name="fighter">Fighter to hold.</param>
    private static void HoldNonterminal(BattleFighter? fighter)
    {
        if (fighter is null)
        {
            throw new InvalidOperationException("fixture fighter unavailable");
        }

        fighter.Hp = fighter.MaxHp = 9000;
    }

    /// <summary>Puts a fighter exactly at the source weak threshold, HP equal to a quarter of its maximum.</summary>
    /// <param name="fighter">Fighter to weaken.</param>
    private static void SetQuarterHp(BattleFighter fighter) => fighter.Hp = fighter.MaxHp / 4;

    /// <summary>Returns the fixture enemy of a slot.</summary>
    /// <param name="slot">Enemy slot.</param>
    /// <returns>The fighter.</returns>
    /// <exception cref="InvalidOperationException">The slot is empty.</exception>
    private BattleFighter Enemy(int slot) => Host.Flow.State.Enemies[slot] ?? throw new InvalidOperationException($"fixture enemy slot {slot} is empty");

    /// <summary>Plays encounter 4's voluntary switch, then a replacement after a real knockout.</summary>
    /// <param name="driver">Production frame driver.</param>
    /// <param name="proof">Entrance call proof.</param>
    /// <param name="root">Capture directory.</param>
    /// <returns>A task completing after both entrances and their command turns passed.</returns>
    private async Task EnemySwitchAndReplacementAsync(BattleProductionFrameDriver driver, BattleEntranceCallProof proof, string root)
    {
        Host.StartEncounterForProbe(SwitchEncounter);
        var enemies = Host.Flow.State.Enemies;
        ProbeRequirement.Require(enemies[0]?.Id == 222 && enemies[1]?.Id == 77 && enemies[2]?.Id == 119, $"encounter 4 enemies=[{string.Join(",", enemies.Select(enemy => enemy?.Id ?? 0))}]");
        HoldNonterminal(Host.Flow.State.ActivePlayerFighter);
        HoldNonterminal(enemies[0]);
        SetQuarterHp(Enemy(1));
        QuietLog.Info("battle-actor-entrance fixture encounter=4 player=9000 enemy222=9000 enemy77=quarter");
        await PlayUntilEntranceAsync(driver);
        await proof.RunAsync(new BattleEntranceExpectation("enemy-switch", BattleEntranceKind.EnemySwitch, 222, 77, BattleWeakIdleRule.Normal, EnemySwitchConfirmAfter));
        await RequireCommandCameraAfterReleaseAsync(driver, root, "enemy-switch");

        // The source knockout event scans from slot 0, so the switched-out 222 comes back before 119.
        Enemy(1).Hp = 1;
        SetQuarterHp(Enemy(0));
        QuietLog.Info("battle-actor-entrance fixture enemy77=1 enemy222=quarter");
        await PlayUntilEntranceAsync(driver);
        ProbeRequirement.Require(Enemy(1).IsKnockedOut, "the replacement did not follow a real knockout");
        await proof.RunAsync(new BattleEntranceExpectation("enemy-replacement", BattleEntranceKind.EnemyReplacement, 77, 222, BattleWeakIdleRule.Weak, EnemyConfirmAfter));
        await RequireCommandCameraAfterReleaseAsync(driver, root, "enemy-replacement");
    }

    /// <summary>Plays the last boss's two special entrances.</summary>
    /// <param name="driver">Production frame driver.</param>
    /// <param name="proof">Entrance call proof.</param>
    /// <returns>A task completing after both entrances passed.</returns>
    private async Task LastBossEntrancesAsync(BattleProductionFrameDriver driver, BattleEntranceCallProof proof)
    {
        Host.StartEncounterForProbe(LastBossEncounter, BattleEntranceExtras.LastBossMusic);
        var enemies = Host.Flow.State.Enemies;
        ProbeRequirement.Require(enemies[1]?.Id == BattleEntranceExtras.StageFighterId && enemies[2]?.Id == BattleEntranceExtras.MusicFighterId, $"encounter 324 enemies=[{string.Join(",", enemies.Select(enemy => enemy?.Id ?? 0))}]");

        // The last boss hits harder than the ordinary fixture's 9000 HP, which knocked the partner out first.
        foreach (BattleFighter member in Host.Flow.State.Players.OfType<BattleFighter>())
        {
            member.Hp = member.MaxHp = LastBossFixtureHp;
        }

        QuietLog.Info($"battle-actor-entrance fixture encounter=324 party={LastBossFixtureHp}");
        int openingStage = Host.Renderer.StageIdForProbe;
        Enemy(0).Hp = 1;
        await PlayUntilEntranceAsync(driver);
        var stageEntrance = await proof.RunAsync(new BattleEntranceExpectation("entrance-1d2", BattleEntranceKind.EnemyReplacement, Enemy(0).Id, BattleEntranceExtras.StageFighterId, BattleWeakIdleRule.Normal, EnemyConfirmAfter));
        BattleSpecialEntranceChecks.RequireStageChange(stageEntrance, openingStage);

        Enemy(1).Hp = 1;
        await PlayUntilEntranceAsync(driver);
        var musicEntrance = await proof.RunAsync(new BattleEntranceExpectation("entrance-1d3", BattleEntranceKind.EnemyReplacement, BattleEntranceExtras.StageFighterId, BattleEntranceExtras.MusicFighterId, BattleWeakIdleRule.Normal, EnemyConfirmAfter), measuredClock: true);
        BattleSpecialEntranceChecks.RequireMusicChange(musicEntrance);
    }

    /// <summary>Plays the battle on the production coordinator, attacking on each command turn, until an entrance
    /// call starts.</summary>
    /// <param name="driver">Production frame driver.</param>
    /// <returns>A task completing once an entrance call holds the battle.</returns>
    private async Task PlayUntilEntranceAsync(BattleProductionFrameDriver driver)
    {
        int tick = 0;
        while (Host.ActorEntranceCall is null)
        {
            tick++;
            ProbeRequirement.Require(tick < 6000 && Host.HasActiveBattle && Host.Flow.Phase != BattleFlowPhase.Ended, $"no entrance started after {tick} ticks phase={Host.Flow.Phase}");
            bool attack = Host.Flow.Phase == BattleFlowPhase.PlayerCommand && Host.Menu.Menu == BattleMenuKind.Root && Host.Menu.Cursor == 0;
            if (attack)
            {
                Host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true });
            }

            driver.Advance(1.0 / 60.0);
            if (attack)
            {
                Host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
            }

            await Host.NextFrameDrawn();
        }
    }

    /// <summary>After an entrance call released the battle, plays until the command turn and checks that its camera
    /// only then takes over.</summary>
    /// <param name="driver">Production frame driver.</param>
    /// <param name="root">Capture directory.</param>
    /// <param name="label">Capture label.</param>
    /// <returns>A task completing once the command turn's camera was observed.</returns>
    private async Task RequireCommandCameraAfterReleaseAsync(BattleProductionFrameDriver driver, string root, string label)
    {
        Vector3 released = Host.Renderer.CameraForProbe.GlobalPosition;
        int tick = 0;
        while (Host.Flow.Phase != BattleFlowPhase.PlayerCommand)
        {
            tick++;
            ProbeRequirement.Require(tick < 6000 && Host.ActorEntranceCall is null, $"{label}: no command turn after the entrance phase={Host.Flow.Phase}");
            driver.Advance(1.0 / 60.0);
            await Host.NextFrameDrawn();
        }

        driver.Advance(1.0 / 60.0);
        await Host.NextFrameDrawn();
        string shot = Host.Renderer.ShotCameraSummary();
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Root && !Host.Renderer.ActorScene.Active, $"{label}: command turn menu={Host.Menu.Menu} owned={Host.Renderer.ActorScene.Active}");
        Image image = Host.CaptureViewport();
        ProbeRequirement.Require(image.SavePng(Path.Combine(root, $"{label}-command.png")) == Error.Ok, $"{label}: command capture failed");
        QuietLog.Info($"battle-actor-entrance {label} command-turn shot={shot} camera-from={released} camera-now={Host.Renderer.CameraForProbe.GlobalPosition}");
    }
}
