using System.Numerics;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class LegacyBattleConsoleTests
{
    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        AdjustDamageBaseValue();
        AdjustDamageFamilyBonus();
        AdjustDamageDamageCut();
        AdjustDamageFiveTimesCap();
        AdjustDamage9999Cap();
        AdjustDamageElementResist();
        AdjustDamageTripleHit();
        AdjustDamageCritical();
        GetElementBoostBoostedAndOpposed();
        ComputeDamagePlayerPhysical();
        ComputeDamageEnemyStrengthFactor();
        ComputeMagicDamageResistBranches();
        GetTurnDelayDeterministic();
        TestRunAwayBlockAndChance();
        Ps1BattlePlacementUsesSourceAxisAndScale();
        Ps1CameraViewCancelsSourceViewSigns();
        Ps1ProjectionDefinesVerticalFov();
        PartnerViewProjectionAndCameraIndexMatchSource();
        StageFadeMatchesStageC();
        Ps1BoneRotationUsesZyxAndScale();
        Ps1HudUsesVerticalFitAndCentered4By3Area();
        ShotCameraStartsWithTheSourceOpeningList();
        ShotCameraSelectsTheInjectedNextList();
        KotemonMotionOneLoopsAtSourceSentinel();
        AttackBlendsThenReturnsToIdle();
        OrderingTableMatchesAddPrim();
        SourceWindingUsesOriginalCornerOrder();
        DialogueProbePinsArrowBlinkFrame();
        BattleProbePinsHudAnimationFrame();

        InitFightersLoadsHpMp();
        InitFightersFirstPartnerIsActiveDigimon();
        EnemyStatsScaledByStrength();

        AttackReducesHpExactly();
        TurnOrderFasterSideActsMore();
        VictoryWhenAllEnemiesKo();
        DefeatWhenAllPlayersKo();
        NoAutoSwitchOnKnockOut();
        ManualSwitchChangesActive();
        SwitchRefusesKnockedOut();
        DigivolveChangesFormNoMp();
        DigivolveBlockedByNoDigivolve();
        DigivolveRefusesCurrentForm();
        ItemHealsAndCaps();
        ItemRevivesParty();
        FleeSucceedsDeterministic();
        FleeBlockedDoesNotFlee();
        TechniqueConsumesMp();
        TechniqueInsufficientMpSpendsNothing();

        EnemyConditionAlwaysAndChance();
        EnemyConditionHpShare();
        EnemyConditionMp();
        EnemyConditionPlayerHp();
        EnemyConditionStarterAndAsleep();
        EnemyConditionOtherEnemy();
        EnemyConditionBoostBattleKind();
        EnemyConditionChargeBoostsTurns();
        PickActionEmptyTable();
        PickActionSingleFallback();
        PickActionFallbackWhenConditionsFail();
        PickActionFirstHoldingCondition();
        PickActionTechniqueOnLowHp();
        PickCounterNoneWithoutCounter();
        PickCounterAttackWhenConditionHolds();
        PickCounterTechnique();
        PickCounterConditionFails();
        ResolveSwitchFixedAndAny();
    }

    private static void Ps1BattlePlacementUsesSourceAxisAndScale()
    {
        var player = BattleSceneMath.FighterPlacement(enemy: false, distance: 192, height: 0);
        var enemy = BattleSceneMath.FighterPlacement(enemy: true, distance: 1408, height: 0);
        Check(
            player.Position.Equals(new Vector3(0, 0, 20.75f)),
            "Kotemon uses the source -0x1400-distance position converted to Godot Z");
        Check(
            enemy.Position.Equals(new Vector3(0, 0, -25.5f)),
            "fighter 51 uses the source +0x1400+distance position converted to Godot Z");
        Check(
            MathF.Abs(player.YawRadians - MathF.PI) < 0.0001f && MathF.Abs(enemy.YawRadians) < 0.0001f,
            "the player alone receives the source half-turn so both camps face each other");
    }

    private static void Ps1ProjectionDefinesVerticalFov()
    {
        var expected = 2f * MathF.Atan(120f / 280f) * 180f / MathF.PI;
        Check(
            MathF.Abs(BattleSceneMath.VerticalFovDegrees(280) - expected) < 0.0001f,
            "the vertical FOV derives from the PS1 240-line projection distance");
    }

    private static void PartnerViewProjectionAndCameraIndexMatchSource()
    {
        var expected = 2f * MathF.Atan(30f / 280f) * 180f / MathF.PI;
        Check(
            MathF.Abs(BattleSceneMath.VerticalFovDegrees(280, 30f) - expected) < 0.0001f,
            "partner view FOV uses its 60-pixel target half-height, not the 240-line screen");
        Check(
            BattleSceneMath.PartnerCameraViewIndex(0) == 6,
            "fighter_camera.c chooses view 6 when idleMotion is zero");
        Check(
            BattleSceneMath.PartnerCameraViewIndex(1) == 7,
            "fighter_camera.c chooses view 7 when idleMotion is non-zero");
    }

    private static void Ps1CameraViewCancelsSourceViewSigns()
    {
        var camera = BattleSceneMath.CameraPosition(-2432, 1920, 11136);
        Check(
            camera == new System.Numerics.Vector3(-9.5f, 7.5f, 43.5f),
            "GsRVIEW2 Y/Z negation and PS1-to-Godot Y/Z negation cancel for camera records");
    }

    private static void StageFadeMatchesStageC()
    {
        var step = BattleSceneMath.StageFadeStep(60);
        var fade = 0;
        for (var frame = 0; frame < 60; frame++)
        {
            fade = BattleSceneMath.AdvanceStageFade(fade, step, 1);
        }

        Check(step == 68 && fade == 4080, $"stage fade after 60 frames step={step} fade={fade}");
        fade = BattleSceneMath.AdvanceStageFade(fade, step, 1);
        Check(fade == 0x1000, $"stage fade clamps at 0x1000 fade={fade}");
        Check(BattleSceneMath.LerpFixed(0, 0x80, 0x800) == 0x40, "stage colour lerp midpoint");
    }

    private static void Ps1BoneRotationUsesZyxAndScale()
    {
        var identity = BattleSceneMath.Ps1BoneBasis(new Vector3(0, 0, 0), new Vector3(4096, 4096, 4096));
        Check(MaxElementDelta(identity, Matrix4x4.Identity) < 0.0001f, "identity PS1 bone transform stays identity");
        var scaled = BattleSceneMath.Ps1BoneBasis(new Vector3(0x123, 0x345, 0x567), new Vector3(4096, 2048, 8192));
        Check(
            MathF.Abs(scaled.M11) > 0.01f && MathF.Abs(scaled.M22) > 0.01f && MathF.Abs(scaled.M33) > 0.01f,
            "PS1 ZYX transform keeps all scaled basis axes");
    }

    private static float MaxElementDelta(Matrix4x4 a, Matrix4x4 b) => new[]
    {
        MathF.Abs(a.M11 - b.M11), MathF.Abs(a.M12 - b.M12), MathF.Abs(a.M13 - b.M13), MathF.Abs(a.M14 - b.M14),
        MathF.Abs(a.M21 - b.M21), MathF.Abs(a.M22 - b.M22), MathF.Abs(a.M23 - b.M23), MathF.Abs(a.M24 - b.M24),
        MathF.Abs(a.M31 - b.M31), MathF.Abs(a.M32 - b.M32), MathF.Abs(a.M33 - b.M33), MathF.Abs(a.M34 - b.M34),
        MathF.Abs(a.M41 - b.M41), MathF.Abs(a.M42 - b.M42), MathF.Abs(a.M43 - b.M43), MathF.Abs(a.M44 - b.M44),
    }.Max();

    private static void Ps1HudUsesVerticalFitAndCentered4By3Area()
    {
        Check(
            BattleHudLayout.Scale == 3 && BattleHudLayout.OffsetX == 160 && BattleHudLayout.OffsetY == 0,
            "the battle HUD uses the shared 3x vertical-fit map with 160px 16:9 side margins");
        Check(
            BattleHudLayout.ToViewport(0, 0) == (160, 0) && BattleHudLayout.ToViewport(320, 240) == (1120, 720),
            "the PS1 screen maps exactly to viewport bounds at 3x");
        Check(
            BattleHudLayout.FitsSourceRect(11, 188, 298, 40) && !BattleHudLayout.FitsSourceRect(11, 188, 310, 40),
            "source HUD bounds reject an element that would be clipped at the PS1 edge");
    }

    private static void ShotCameraStartsWithTheSourceOpeningList()
    {
        var camera = new BattleShotCamera(new SeqShotRng(0), enemyCameraCount: 3);
        Check(
            camera.List == 3 && camera.Shot == 0 && camera.Substate == 10 && camera.Time == 90,
            "shot camera starts list 3 shot 0 as the enemy view for 90 PAL frames");
        camera.StepFrames(90);
        Check(
            camera.List == 3 && camera.Shot == 1 && camera.Substate == 5 && camera.Time == 90,
            "after 90 frames shot camera enters source substate 5");
        camera.Step();
        Check(
            camera.View.RotationY == 0xE2,
            "substate 5 adds 0xE3 then spins back one PAL frame");
    }

    private static void ShotCameraSelectsTheInjectedNextList()
    {
        var camera = new BattleShotCamera(new SeqShotRng(2), enemyCameraCount: 3);
        camera.StepFrames(180);
        Check(
            camera.List == 2 && camera.Shot == 0 && camera.Substate == 1 && camera.Time == 1920,
            "at 180 frames list 3 selects its injected third successor list 2");
    }

    private static void KotemonMotionOneLoopsAtSourceSentinel()
    {
        var machine = new BattleMotionMachine(KotemonMotions());
        var expected = Enumerable.Range(10, 61).Append(0x8000).ToArray();
        Check(machine.Keyframes.SequenceEqual(expected), "Kotemon motion_1_steps expands to 10..70 then the 0x8000 loop sentinel");
        for (var i = 0; i < 60; i++)
        {
            machine.Step(1);
        }

        Check(machine.Looping && machine.Keyframe == 0, $"motion 1 resolves its sentinel back to keyframe zero actual={machine.Keyframe} last={machine.Keyframes[^1]}");
        machine.Step(1);
        Check(machine.Frame == 10 && !machine.MotionDone, "idle resumes from frame 10 after the loop");
    }

    private static void AttackBlendsThenReturnsToIdle()
    {
        var machine = new BattleMotionMachine(KotemonMotions());
        machine.SetMotion(3, restart: true);
        for (var i = 0; i < 11; i++)
        {
            machine.Step(1);
        }

        Check(machine.Blending && machine.ToIdle && machine.BlendTarget == 10, "attack motion enters its source blend toward idle frame 10");
        for (var i = 0; i < 40 && !machine.MotionDone; i++)
        {
            machine.Step(1);
        }

        Check(machine.MotionDone, "attack reaches its source 0xffff terminal");
        machine.Step(1);
        Check(machine.Motion == 1 && !machine.MotionDone, "a completed idling model returns to motion 1");
    }

    private static void OrderingTableMatchesAddPrim()
    {
        // layer.c: DrawOTag traverses the table from its last entry down. addPrim prepends,
        // so a larger key draws first and a later primitive wins inside its equal-key bucket.
        var order = RenderOrderingTable.DrawOrder([5, 9, 5, 1]);
        Check(order.SequenceEqual([1, 2, 0, 3]), $"OT draw order={string.Join(",", order)}");
        Check(
            RenderOrderingTable.DepthBucketCount == 0x1000 && RenderOrderingTable.DepthShift(RenderOrderingTable.DepthBucketShift) == 2,
            "otShift 12 selects 0x1000 buckets and projectMesh shifts Z by two");
        Check(
            RenderOrderingTable.TriangleKey(400, 800, 1200, shift: 2) == RenderOrderingTable.TriangleKey(800, 800, 800, shift: 2),
            "average Z key is order independent");
        Check(
            RenderOrderingTable.TriangleKey(4096, 8192, 12288, shift: 2) == 2047,
            "AVSZ3 uses ZSF3 0x555 rather than an imprecise integer divide");
        Check(
            RenderOrderingTable.QuadKey(4096, 4096, 4096, 4096, shift: 2) == 1024,
            "AVSZ4 uses ZSF4 0x400");
        Check(
            RenderOrderingTable.FacesCamera(new(0, 0), new(10, 0), new(0, 10)) != RenderOrderingTable.FacesCamera(new(0, 0), new(0, 10), new(10, 0)),
            "nclip sign flips with winding");
        Check(!RenderOrderingTable.FacesCamera(new(0, 0), new(0, 0), new(0, 10)), "nclip rejects coincident screen points");
        Check(
            !RenderOrderingTable.FacesCamera(new(0, 0), new(10, 0), new(0, 10), new(10, 0)),
            "nclip rejects a quad whose fourth screen point coincides");
        Check(
            RenderOrderingTable.WholeQuadIndices().SequenceEqual([0, 1, 2, 2, 1, 3]),
            "a source quad remains one ordered primitive and emits its two triangles only after ordering");
    }

    private static void SourceWindingUsesOriginalCornerOrder()
    {
        var first = new Vector2(0, 0);
        var second = new Vector2(10, 0);
        var third = new Vector2(0, 10);
        Check(BattlePrimitiveCompositor.FacesCameraInSourceOrder(first, second, third), "source corner order (0,1,2) preserves the visible face");
        var invertedSecond = third;
        var invertedThird = second;
        Check(!BattlePrimitiveCompositor.FacesCameraInSourceOrder(first, invertedSecond, invertedThird), "inverted source corner order (0,2,1) rejects the visible face");
    }

    private static void DialogueProbePinsArrowBlinkFrame()
    {
        Check(
            DialogueRenderer.ProbeArrowBrightnessIndex(0) == 0,
            "a deterministic dialogue capture starts at the first arrow brightness");
        Check(
            DialogueRenderer.ProbeArrowBrightnessIndex(3) == 3,
            "a deterministic dialogue capture can select every arrow brightness");
        Check(
            DialogueRenderer.ProbeArrowBrightnessIndex(4) == 0,
            "the deterministic dialogue arrow brightness wraps after four frames");
        Assert.Throws<ArgumentOutOfRangeException>(() => DialogueRenderer.ProbeArrowBrightnessIndex(-1));
    }

    private static void BattleProbePinsHudAnimationFrame()
    {
        Check(
            BattleRenderer.ProbeAnimationMilliseconds(0) == 0,
            "a deterministic battle capture starts at the first HUD animation frame");
        Check(
            BattleRenderer.ProbeAnimationMilliseconds(11) == 176,
            "a deterministic battle capture maps the HUD animation frame to source milliseconds");
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleRenderer.ProbeAnimationMilliseconds(-1));
    }

    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> KotemonMotions() =>
    [
        [[0, 61, 10, 70], [61, 0, -32768, 0], [32767, 0, 0, 0]],
        [[0, 91, 90, 180], [91, 0, -32768, 0], [32767, 0, 0, 0]],
        [[0, 10, 0, 0], [10, 1, 200, 200], [11, 20, 0, 0], [31, 0, -1, 0], [32767, 0, 0, 0]],
    ];

    // ---- DamageCalculator: adjustDamage ----
    private static void AdjustDamageBaseValue()
    {
        int dmg = DamageCalculator.AdjustDamage(Tech(), Stats(), Stats(), 100, new ElementBoost(0, 0), -1);
        Check(dmg == 100, "no modifiers leaves the base value untouched");
    }

    private static void AdjustDamageFamilyBonus()
    {
        var tech = Tech(family: 2);
        var target = Stats(family: 2);
        int dmg = DamageCalculator.AdjustDamage(tech, Stats(), target, 100, new ElementBoost(0, 0), -1);
        Check(dmg == 150, "matching family adds half again (+50%)");
    }

    private static void AdjustDamageDamageCut()
    {
        var target = Stats();
        target.DamageCut = 30;
        int dmg = DamageCalculator.AdjustDamage(Tech(), Stats(), target, 100, new ElementBoost(0, 0), -1);
        Check(dmg == 70, "damage cut subtracts from each hit");
    }

    private static void AdjustDamageFiveTimesCap()
    {
        var tech = Tech(element: 2, elementPower: 10);
        var target = Stats();
        target.Resist[0] = 2;
        int dmg = DamageCalculator.AdjustDamage(tech, Stats(), target, 100, new ElementBoost(0, 0), -1);
        Check(dmg == 500, "result is capped at 5x the base value");
    }

    private static void AdjustDamage9999Cap()
    {
        var tech = Tech(element: 2, elementPower: 10);
        var target = Stats();
        target.Resist[0] = 1;
        int dmg = DamageCalculator.AdjustDamage(tech, Stats(), target, 3000, new ElementBoost(0, 0), -1);
        Check(dmg == 9999, "EU/JP caps damage at 9999");
    }

    private static void AdjustDamageElementResist()
    {
        var tech = Tech(element: 2, elementPower: 5);
        var target = Stats();
        target.Resist[0] = 10;
        int dmg = DamageCalculator.AdjustDamage(tech, Stats(), target, 100, new ElementBoost(0, 0), -1);
        Check(dmg == 200, "elementPower * 2 / resist adds to the damage");
    }

    private static void AdjustDamageTripleHit()
    {
        var user = Stats();
        user.TripleHit = 1;
        int dmg = DamageCalculator.AdjustDamage(Tech(), user, Stats(), 100, new ElementBoost(0, 0), -1);
        Check(dmg == 40, "a plain technique under tripleHit deals 0.4 of the damage");
    }

    private static void AdjustDamageCritical()
    {
        int dmg = DamageCalculator.AdjustDamage(Tech(), Stats(), Stats(), 100, new ElementBoost(0, 0), 0);
        Check(dmg == 150, "a critical at roll 0 adds 0x20/64 (half) of the damage");
    }

    private static void GetElementBoostBoostedAndOpposed()
    {
        Check(DamageCalculator.GetElementBoost(100, 2, new ElementBoost(2, 128)) == 100, "boosted element adds amount/128 of the value");
        Check(DamageCalculator.GetElementBoost(100, 3, new ElementBoost(2, 128)) == -50, "opposed element (3 vs boost 2) takes half off");
        Check(DamageCalculator.GetElementBoost(100, 2, new ElementBoost(0, 128)) == 0, "no boost element adds nothing");
        Check(DamageCalculator.GetElementBoost(100, 6, new ElementBoost(2, 128)) == 0, "an unrelated element is untouched");
    }

    private static void ComputeDamagePlayerPhysical()
    {
        var user = Stats(attack: 50);
        var target = Stats(defense: 10);
        int dmg = DamageCalculator.ComputeDamage(Tech(power: 20), user, target, false, 0, new ElementBoost(0, 0), -1);
        Check(dmg == 100, "power * attack / defense is the physical base");
    }

    private static void ComputeDamageEnemyStrengthFactor()
    {
        var user = Stats(attack: 50);
        var target = Stats(defense: 20);
        int dmg = DamageCalculator.ComputeDamage(Tech(power: 10), user, target, true, 32, new ElementBoost(0, 0), -1);
        Check(dmg == 50, "an enemy's hit is scaled once more by strength/16");
    }

    private static void ComputeMagicDamageResistBranches()
    {
        var user = Stats(spirit: 50);
        var target = Stats(spirit: 50);
        target.Resist[0] = 200;
        int dmg = DamageCalculator.ComputeMagicDamage(Tech(power: 100, element: 2), user, target, false, 0, new ElementBoost(0, 0), -1);
        Check(dmg == 75, "magic resist 200 lands in the 100..299 branch (125 - resist/4)%");
    }

    private static void GetTurnDelayDeterministic()
    {
        Check(DamageCalculator.GetTurnDelay(100, 100) == 1000, "equal speeds give a delay of 1000");
        Check(
            DamageCalculator.GetTurnDelay(200, 100) < DamageCalculator.GetTurnDelay(100, 100),
            "a faster side gets a smaller (sooner) delay");
    }

    private static void TestRunAwayBlockAndChance()
    {
        Check(!RunAwayRules.TestRunAway(0, Stats(), Stats(), true, 0, 0), "run-away block always fails");
        Check(!RunAwayRules.TestRunAway(0, Stats(), Stats(), false, FighterFlags.Asleep, 0), "asleep can't run away");
        Check(RunAwayRules.TestRunAway(0, Stats(), Stats(), false, 0, 0), "roll 0 is under the base chance 8");
        Check(!RunAwayRules.TestRunAway(0, Stats(), Stats(), false, 0, 8), "roll 8 is not under the base chance 8");
    }

    // ---- InitFighters ----
    private static void InitFightersLoadsHpMp()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 200, Mp = 30, MaxMp = 40, Stats = Stats() };
        setup.Partners[1] = new PartnerSeed { Id = 2, Hp = 10, MaxHp = 20, Mp = 3, MaxMp = 4, Stats = Stats() };
        setup.Partners[2] = new PartnerSeed { Id = 3, Hp = 5, MaxHp = 6, Mp = 7, MaxMp = 8, Stats = Stats() };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 50, Mp = 60, Strength = 16, BaseStats = Stats() };
        setup.Enemies[1] = new BattleEnemy { Fighter = 102, Hp = 70, Mp = 80, Strength = 16, BaseStats = Stats() };
        setup.Enemies[2] = new BattleEnemy { Fighter = 103, Hp = 90, Mp = 100, Strength = 16, BaseStats = Stats() };

        var state = BattleState.InitFighters(setup, digimon: 999);

        Check(state.Players[0]!.Id == 999, "slot 0's id is the active digimon argument");
        Check(
            state.Players[0]!.Hp == 100 && state.Players[0]!.MaxHp == 200 && state.Players[0]!.Mp == 30 && state.Players[0]!.MaxMp == 40,
            "partner HP/MP are loaded from the party seed");
        Check(state.Players[1]!.Id == 2 && state.Players[2]!.Id == 3, "partner slots 1 and 2 use their seed ids");
        Check(
            state.Enemies[0]!.Id == 101 && state.Enemies[0]!.Hp == 50 && state.Enemies[0]!.MaxHp == 50,
            "enemy HP and max HP are both the setup value");
        Check(state.Enemies[0]!.Mp == 60 && state.Enemies[0]!.MaxMp == 60, "enemy MP and max MP are both the setup value");
        Check(state.Enemies[2]!.Id == 103, "all three enemies are loaded");
        Check(state.ActivePlayer == 0 && state.ActiveEnemy == 0, "the first partner and first enemy are active");
        Check(state.Outcome == BattleOutcome.None, "a populated battle starts unresolved");
    }

    private static void InitFightersFirstPartnerIsActiveDigimon()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats() };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 50, Mp = 0, Strength = 16, BaseStats = Stats() };
        var state = BattleState.InitFighters(setup, digimon: 42);
        Check(state.Players[0]!.Id == 42, "the digimon argument overrides slot 0's seed id");
    }

    private static void EnemyStatsScaledByStrength()
    {
        var raw = Stats(attack: 100, defense: 50, spirit: 40, wisdom: 30, speed: 20, family: 3);
        var resolved = DamageCalculator.ComputeEnemyStats(raw, level: 5, strength: 32);
        Check(resolved.Stats[BattleStat.Attack] == 200, "enemy attack scales by strength/16");
        Check(resolved.Stats[BattleStat.Defense] == 100, "enemy defense scales by strength/16");
        Check(resolved.Stats[BattleStat.Speed] == 40, "enemy speed scales by strength/16");
        Check(resolved.Level == 5 && resolved.Family == 3, "level and family are copied through");
    }

    // ---- Turn loop ----
    private static void AttackReducesHpExactly()
    {
        var state = SimpleBattle(new BattleFixture(PlayerAttack: 50, EnemyRawDefense: 10, EnemyHp: 500));
        state.PlayerAttack(Tech(power: 20), new NoRng());
        Check(state.Enemies[0]!.Hp == 400, "a 100-damage hit leaves 400 of 500 HP");
    }

    private static void TurnOrderFasterSideActsMore()
    {
        var state = SimpleBattle(new BattleFixture(PlayerAttack: 10, PlayerSpeed: 200, EnemyRawAttack: 10, EnemySpeed: 100));
        var zero = Tech(power: 0);
        int playerTurns = 0, enemyTurns = 0;
        for (int i = 0; i < 6; i++)
        {
            if (state.Outcome != BattleOutcome.None)
            {
                break;
            }

            if (state.NextActor == BattleSide.Player)
            {
                state.PlayerAttack(zero, new NoRng());
                playerTurns++;
            }
            else
            {
                state.EnemyAttack(zero, new NoRng());
                enemyTurns++;
            }
        }

        Check(playerTurns > enemyTurns, "the faster player acts more often over six turns");
        Check(playerTurns + enemyTurns == 6, "the loop resolves six actions");
    }

    private static void VictoryWhenAllEnemiesKo()
    {
        var state = SimpleBattle(new BattleFixture(PlayerAttack: 50, EnemyRawDefense: 10, EnemyHp: 50));
        state.PlayerAttack(Tech(power: 20), new NoRng());   // 100 damage, KO
        Check(state.Outcome == BattleOutcome.Won, "knocking out every enemy wins the battle");
        Check(state.Enemies[0]!.Hp == 0, "the enemy HP clamps to zero");
    }

    private static void DefeatWhenAllPlayersKo()
    {
        var state = SimpleBattle(new BattleFixture(PlayerDefense: 10, PlayerHp: 50, EnemyRawAttack: 100, EnemyStrength: 16));
        state.EnemyAttack(Tech(power: 10), new NoRng());    // 10 * 16/16 * 100 / 10 = 100 damage, KO
        Check(state.Outcome == BattleOutcome.Lost, "knocking out every partner loses the battle");
        Check(state.Players[0]!.Hp == 0, "the partner HP clamps to zero");
    }

    private static void NoAutoSwitchOnKnockOut()
    {
        var state = TwoPartnerBattle();
        state.EnemyAttack(Tech(power: 10), new NoRng());    // 100 damage against the 50-HP active partner
        Check(state.Players[0]!.IsKnockedOut, "the active partner is knocked out");
        Check(state.ActivePlayer == 0, "the active slot does not auto-switch to the next partner");
        Check(state.Outcome == BattleOutcome.None, "the battle is not lost while another partner lives");
    }

    private static void ManualSwitchChangesActive()
    {
        var state = TwoPartnerBattle();
        bool switched = state.PlayerSwitch(1);
        Check(switched, "switching to a living partner succeeds");
        Check(state.ActivePlayer == 1, "the active partner changes to the picked slot");
        Check(state.NextPlayerTurn > 0, "the switch consumes the turn (the cadence is re-armed)");
    }

    private static void SwitchRefusesKnockedOut()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats(defense: 100) };
        setup.Partners[1] = new PartnerSeed { Id = 2, Hp = 0, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats(defense: 100) };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats() };
        var state = BattleState.InitFighters(setup, digimon: 999);
        bool switched = state.PlayerSwitch(1);
        Check(!switched, "switching to a knocked-out partner is refused");
        Check(state.ActivePlayer == 0, "the active partner is unchanged");
    }

    private static void DigivolveChangesFormNoMp()
    {
        var state = SimpleBattle(new BattleFixture(PlayerMp: 30));
        bool changed = state.PlayerDigivolve(555);
        Check(changed, "digivolving into another form succeeds");
        Check(state.Players[0]!.Id == 555, "the active partner changes form");
        Check(state.Players[0]!.Mp == 30, "the Digivolve command costs no MP (WFIGHTMN_digivolve)");
    }

    private static void DigivolveBlockedByNoDigivolve()
    {
        var state = SimpleBattle();
        state.Players[0]!.Flags |= FighterFlags.NoDigivolve;
        bool changed = state.PlayerDigivolve(555);
        Check(!changed, "FIGHTER_NO_DIGIVOLVE bars the Digivolve command");
        Check(state.Players[0]!.Id == 999, "the form is unchanged");
    }

    private static void DigivolveRefusesCurrentForm()
    {
        var state = SimpleBattle();
        bool changed = state.PlayerDigivolve(999);   // the active form is already 999
        Check(!changed, "picking the current form is refused (digivolve_menu.c)");
    }

    private static void ItemHealsAndCaps()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 50, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats() };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats() };
        var state = BattleState.InitFighters(setup, digimon: 999);
        bool used = state.PlayerItem(new BattleItem(43, "heal", "partner", 500));
        Check(used, "a heal item is usable");
        Check(state.Players[0]!.Hp == 100, "heal caps at max HP (50 + 500 -> 100)");
    }

    private static void ItemRevivesParty()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats() };
        setup.Partners[1] = new PartnerSeed { Id = 2, Hp = 0, MaxHp = 200, Mp = 10, MaxMp = 10, Stats = Stats() };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats() };
        var state = BattleState.InitFighters(setup, digimon: 999);
        bool used = state.PlayerItem(new BattleItem(0x46, "revive", "party", 0));
        Check(used, "a revive item is usable");
        Check(state.Players[1]!.Hp == 200, "revive restores knocked-out partners to max HP");
    }

    private static void FleeSucceedsDeterministic()
    {
        var state = SimpleBattle();
        bool fled = state.PlayerFlee(new NoRng());   // rng 0 < (0+1)*8 = 8
        Check(fled, "a roll of 0 under the base flee chance escapes");
        Check(state.Outcome == BattleOutcome.PlayerFled, "escaping ends the battle as player fled");
        Check(state.RunAttempts == 1, "each flee attempt increments runAttempts");
    }

    private static void FleeBlockedDoesNotFlee()
    {
        var state = SimpleBattle();
        state.Blocks[BattleConst.BlockRunAway] = 1;
        bool fled = state.PlayerFlee(new NoRng());
        Check(!fled && state.Outcome == BattleOutcome.None, "the run-away block keeps the battle going");
    }

    private static void TechniqueConsumesMp()
    {
        var state = SimpleBattle(new BattleFixture(PlayerAttack: 50, PlayerMp: 30, EnemyRawDefense: 10, EnemyHp: 500));
        state.PlayerTechnique(Tech(power: 20, mp: 10), new NoRng());
        Check(state.Players[0]!.Mp == 20, "a technique subtracts its MP cost");
        Check(state.Enemies[0]!.Hp == 400, "a physical technique deals power * attack / defense");
    }

    private static void TechniqueInsufficientMpSpendsNothing()
    {
        var state = SimpleBattle(new BattleFixture(PlayerMp: 5, EnemyHp: 500));
        bool used = state.PlayerTechnique(Tech(power: 20, mp: 10), new NoRng());
        Check(!used, "a technique the partner can't afford is not used");
        Check(state.Players[0]!.Mp == 5 && state.Enemies[0]!.Hp == 500, "nothing is spent and no damage is dealt");
    }

    // ---- BattleEnemyAi (enemy_turn.c / counterattack.c) ----
    private static void EnemyConditionAlwaysAndChance()
    {
        Check(BattleEnemyAi.TestEnemyCondition(0, 0, Ctx(Enemy()), new NoRng()), "condition 0 always holds");
        Check(BattleEnemyAi.TestEnemyCondition(1, 10, Ctx(Enemy()), new NoRng()), "condition 1 holds on a roll of 0 under the chance");
        Check(!BattleEnemyAi.TestEnemyCondition(1, 10, Ctx(Enemy()), new SeqRng(10)), "condition 1 fails on a roll of exactly the chance");
    }

    private static void EnemyConditionHpShare()
    {
        var low = Enemy(hp: 49, maxHp: 100);
        var high = Enemy(hp: 50, maxHp: 100);
        Check(BattleEnemyAi.TestEnemyCondition(2, 64, Ctx(low), new NoRng()), "condition 2 holds when the enemy is under half HP");
        Check(!BattleEnemyAi.TestEnemyCondition(2, 64, Ctx(high), new NoRng()), "condition 2 fails at exactly half HP");
        Check(BattleEnemyAi.TestEnemyCondition(3, 64, Ctx(high), new NoRng()), "condition 3 holds at half HP and above");
        Check(!BattleEnemyAi.TestEnemyCondition(3, 64, Ctx(low), new NoRng()), "condition 3 fails under half HP");
    }

    private static void EnemyConditionMp()
    {
        Check(BattleEnemyAi.TestEnemyCondition(4, 50, Ctx(Enemy(mp: 49)), new NoRng()), "condition 4 holds when MP is under the argument");
        Check(BattleEnemyAi.TestEnemyCondition(5, 50, Ctx(Enemy(mp: 50)), new NoRng()), "condition 5 holds when MP is at least the argument");
        Check(!BattleEnemyAi.TestEnemyCondition(5, 50, Ctx(Enemy(mp: 49)), new NoRng()), "condition 5 fails under the argument");
    }

    private static void EnemyConditionPlayerHp()
    {
        var low = new BattleFighter { Id = 1, Hp = 49, MaxHp = 100 };
        var high = new BattleFighter { Id = 1, Hp = 50, MaxHp = 100 };
        Check(BattleEnemyAi.TestEnemyCondition(6, 64, Ctx(Enemy(), low), new NoRng()), "condition 6 holds when the partner is under half HP");
        Check(BattleEnemyAi.TestEnemyCondition(7, 64, Ctx(Enemy(), high), new NoRng()), "condition 7 holds when the partner is at half HP or more");
    }

    private static void EnemyConditionStarterAndAsleep()
    {
        var starter = new BattleFighter { Id = 3 };
        var other = new BattleFighter { Id = 99 };
        var asleep = new BattleFighter { Id = 3, Flags = FighterFlags.Asleep };
        Check(BattleEnemyAi.TestEnemyCondition(8, 0, Ctx(Enemy(), starter, starters: [3, 9]), new NoRng()), "condition 8 holds when the partner is a starter Digimon");
        Check(!BattleEnemyAi.TestEnemyCondition(8, 0, Ctx(Enemy(), other, starters: [3, 9]), new NoRng()), "condition 8 fails for a non-starter Digimon");
        Check(BattleEnemyAi.TestEnemyCondition(9, 0, Ctx(Enemy(), asleep), new NoRng()), "condition 9 holds when the partner is asleep");
    }

    private static void EnemyConditionOtherEnemy()
    {
        var active = Enemy(id: 101, hp: 500);
        var living = new BattleFighter { Id = 102, Hp = 1 };
        var dead = new BattleFighter { Id = 103, Hp = 0 };
        var row = new BattleFighter?[] { active, living, dead };
        Check(BattleEnemyAi.TestEnemyCondition(10, 0, Ctx(active, enemies: row, activeEnemy: 0), new NoRng()), "condition 10 holds when another enemy is alive");
        Check(BattleEnemyAi.TestEnemyCondition(10, 102, Ctx(active, enemies: row, activeEnemy: 0), new NoRng()), "condition 10 holds for a specific other enemy id");
        Check(!BattleEnemyAi.TestEnemyCondition(10, 103, Ctx(active, enemies: row, activeEnemy: 0), new NoRng()), "condition 10 fails for a dead enemy id");
        Check(!BattleEnemyAi.TestEnemyCondition(10, 0, Ctx(active, enemies: [active], activeEnemy: 0), new NoRng()), "condition 10 fails with no other living enemy");
    }

    private static void EnemyConditionBoostBattleKind()
    {
        var ctx = Ctx(Enemy());
        ctx.BoostElement = 2;
        ctx.BattleId = 7;
        ctx.EncounterKind = 3;
        Check(BattleEnemyAi.TestEnemyCondition(11, 2, ctx, new NoRng()), "condition 11 holds when the boost element matches");
        Check(BattleEnemyAi.TestEnemyCondition(12, 7, ctx, new NoRng()), "condition 12 holds when the battle id matches");
        Check(BattleEnemyAi.TestEnemyCondition(13, 3, ctx, new NoRng()), "condition 13 holds when the encounter kind matches");
        Check(!BattleEnemyAi.TestEnemyCondition(12, 8, ctx, new NoRng()), "condition 12 fails on a different battle id");
    }

    private static void EnemyConditionChargeBoostsTurns()
    {
        var charged = Enemy();
        charged.Charge = 1;
        Check(BattleEnemyAi.TestEnemyCondition(14, 0, Ctx(charged), new NoRng()), "condition 14 holds when the enemy has charge");
        var lowered = Enemy();
        lowered.Boosts[1] = -1;
        Check(BattleEnemyAi.TestEnemyCondition(16, 0, Ctx(lowered), new NoRng()), "condition 16 holds when the enemy's defense boost is lowered");
        Check(!BattleEnemyAi.TestEnemyCondition(15, 0, Ctx(Enemy()), new NoRng()), "condition 15 fails with no lowered attack boost");
        Check(BattleEnemyAi.TestEnemyCondition(18, 2, Ctx(Enemy(turns: 4)), new NoRng()), "condition 18 holds when the turn count is a multiple of the argument");
        Check(!BattleEnemyAi.TestEnemyCondition(18, 2, Ctx(Enemy(turns: 3)), new NoRng()), "condition 18 fails on a non-multiple turn count");
        Check(!BattleEnemyAi.TestEnemyCondition(18, 0, Ctx(Enemy(turns: 0)), new NoRng()), "condition 18 never divides by zero");
    }

    private static void PickActionEmptyTable()
    {
        var enemy = Enemy();
        enemy.Actions = [];
        Check(BattleEnemyAi.PickAction(enemy, Ctx(enemy), new NoRng()).Kind == EnemyActionKind.None, "an empty action table resolves to None");
    }

    private static void PickActionSingleFallback()
    {
        var enemy = Enemy();
        enemy.Techs = [5, 0, 0];
        enemy.Actions = [new BattleTableAction(1, 0, 0)];
        var decision = BattleEnemyAi.PickAction(enemy, Ctx(enemy), new NoRng());
        Check(decision.Kind == EnemyActionKind.Attack && decision.TechId == 5, "a single action is the unconditional fallback (an attack)");
    }

    private static void PickActionFallbackWhenConditionsFail()
    {
        var enemy = Enemy(turns: 3);   // hp 500, mp 100
        enemy.Techs = [5, 6, 7];
        enemy.Actions =
        [
            new BattleTableAction(2, 18, 2),   // technique 1 on even turns -> false (3 % 2 != 0)
            new BattleTableAction(3, 2, 64),   // technique 2 under half HP -> false (500)
            new BattleTableAction(4, 4, 50),   // run away under 50 MP -> false (100)
            new BattleTableAction(1, 0, 0),    // fallback: attack
        ];
        var decision = BattleEnemyAi.PickAction(enemy, Ctx(enemy), new NoRng());
        Check(decision.Kind == EnemyActionKind.Attack && decision.TechId == 5, "the fallback attack is taken when no condition holds");
    }

    private static void PickActionFirstHoldingCondition()
    {
        var enemy = Enemy(turns: 4);   // even turns
        enemy.Techs = [5, 6, 7];
        enemy.Actions =
        [
            new BattleTableAction(2, 18, 2),   // technique 1 on even turns -> true
            new BattleTableAction(1, 0, 0),    // fallback attack
        ];
        var decision = BattleEnemyAi.PickAction(enemy, Ctx(enemy), new NoRng());
        Check(decision.Kind == EnemyActionKind.Technique1 && decision.TechId == 6, "the first holding condition wins (technique 1)");
    }

    private static void PickActionTechniqueOnLowHp()
    {
        var enemy = Enemy(hp: 30, maxHp: 100);   // under half
        enemy.Techs = [5, 6, 7];
        enemy.Actions =
        [
            new BattleTableAction(2, 2, 64),    // technique 1 under half HP -> true
            new BattleTableAction(1, 0, 0),     // fallback attack
        ];
        var decision = BattleEnemyAi.PickAction(enemy, Ctx(enemy), new NoRng());
        Check(decision.Kind == EnemyActionKind.Technique1 && decision.TechId == 6, "a low-HP condition picks the technique");
    }

    private static void PickCounterNoneWithoutCounter()
    {
        var enemy = Enemy();
        enemy.Counter = default;   // condition 0 = no counter
        Check(BattleEnemyAi.PickCounter(enemy, Ctx(enemy), new NoRng()).Kind == EnemyActionKind.None, "a condition-0 counter is no counter");
    }

    private static void PickCounterAttackWhenConditionHolds()
    {
        var enemy = Enemy(hp: 30, maxHp: 100);   // under half
        enemy.Techs = [5, 6, 7];
        enemy.Counter = new BattleTableAction(1, 2, 64);   // counter with the basic attack under half HP
        var decision = BattleEnemyAi.PickCounter(enemy, Ctx(enemy), new NoRng());
        Check(decision.Kind == EnemyActionKind.Attack && decision.TechId == 5, "a held counter condition counter-attacks with the first technique");
    }

    private static void PickCounterTechnique()
    {
        var enemy = Enemy();
        enemy.Techs = [5, 6, 7];
        enemy.Counter = new BattleTableAction(2, 3, 0);   // counter with technique 1, hp >= 0 (always)
        var decision = BattleEnemyAi.PickCounter(enemy, Ctx(enemy), new NoRng());
        Check(decision.Kind == EnemyActionKind.Technique1 && decision.TechId == 6, "a counter target of kind 2 uses the second technique");
    }

    private static void PickCounterConditionFails()
    {
        var enemy = Enemy(hp: 100, maxHp: 100);   // not under half
        enemy.Techs = [5, 6, 7];
        enemy.Counter = new BattleTableAction(1, 2, 64);   // counter under half HP
        Check(BattleEnemyAi.PickCounter(enemy, Ctx(enemy), new NoRng()).Kind == EnemyActionKind.None, "a counter whose condition fails does not trigger");
    }

    private static void ResolveSwitchFixedAndAny()
    {
        var slot0 = new BattleFighter { Id = 102, Hp = 1 };
        var slot1 = new BattleFighter { Id = 103, Hp = 1 };
        var dead = new BattleFighter { Id = 104, Hp = 0 };
        var row = new BattleFighter?[] { slot0, slot1, dead };
        Check(
            BattleEnemyAi.ResolveSwitch(new EnemyDecision(EnemyActionKind.SwitchTo0, 0, 0), row, activeEnemy: 1, new NoRng()) == 0,
            "a fixed switch to a living enemy resolves to that slot");
        Check(
            BattleEnemyAi.ResolveSwitch(new EnemyDecision(EnemyActionKind.SwitchTo2, 0, 2), row, activeEnemy: 0, new NoRng()) == -1,
            "a fixed switch to a dead enemy resolves to no switch");
        Check(
            BattleEnemyAi.ResolveSwitch(new EnemyDecision(EnemyActionKind.SwitchTo0, 0, 0), row, activeEnemy: 0, new NoRng()) == -1,
            "a switch to the already-active slot resolves to no switch");
        Check(
            BattleEnemyAi.ResolveSwitch(new EnemyDecision(EnemyActionKind.SwitchToAny, 0, -1), row, activeEnemy: 0, new NoRng()) == 1,
            "a switch-to-any with one other living enemy picks that slot");
        Check(
            BattleEnemyAi.ResolveSwitch(new EnemyDecision(EnemyActionKind.SwitchToAny, 0, -1), row, activeEnemy: 2, new NoRng()) == 0,
            "a switch-to-any with two living enemies picks the first on a roll of 0");
        Check(
            BattleEnemyAi.ResolveSwitch(new EnemyDecision(EnemyActionKind.SwitchToAny, 0, -1), [slot0], activeEnemy: 0, new NoRng()) == -1,
            "a switch-to-any with no other living enemy resolves to no switch");
    }

    // ---- Fixtures ----
    private static BattleState SimpleBattle(BattleFixture? fixture = null)
    {
        var values = fixture ?? new BattleFixture();
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed
        {
            Id = 1,
            Hp = values.PlayerHp,
            MaxHp = values.PlayerHp,
            Mp = values.PlayerMp,
            MaxMp = values.PlayerMp,
            Stats = Stats(values.PlayerAttack, values.PlayerDefense, speed: values.PlayerSpeed),
        };
        setup.Enemies[0] = new BattleEnemy
        {
            Fighter = 101,
            Hp = values.EnemyHp,
            Mp = values.EnemyMp,
            Strength = values.EnemyStrength,
            BaseStats = Stats(values.EnemyRawAttack, values.EnemyRawDefense, speed: values.EnemySpeed),
        };
        return BattleState.InitFighters(setup, digimon: 999);
    }

    private static BattleState TwoPartnerBattle()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 50, MaxHp = 50, Mp = 10, MaxMp = 10, Stats = Stats(defense: 10) };
        setup.Partners[1] = new PartnerSeed { Id = 2, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats(defense: 10) };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats(attack: 100) };
        return BattleState.InitFighters(setup, digimon: 999);
    }

    private static BattleStats Stats(
        int attack = 0,
        int defense = 0,
        int spirit = 0,
        int wisdom = 0,
        int speed = 0,
        int family = 0)
    {
        var s = new BattleStats();
        s.Stats[BattleStat.Attack] = attack;
        s.Stats[BattleStat.Defense] = defense;
        s.Stats[BattleStat.Spirit] = spirit;
        s.Stats[BattleStat.Wisdom] = wisdom;
        s.Stats[BattleStat.Speed] = speed;
        s.Family = family;
        return s;
    }

    private static TechData Tech(int power = 0, int element = 0, int elementPower = 0, int family = 0, int mp = 0)
    {
        return new TechData
        {
            Power = power,
            Element = element,
            ElementPower = elementPower,
            Family = family,
            Mp = mp,
            Accuracy = 128,
        };
    }

    private static BattleFighter Enemy(int id = 101, int hp = 500, int maxHp = 500, int mp = 100, int turns = 0) => new()
    {
        Id = id,
        Hp = hp,
        MaxHp = maxHp,
        Mp = mp,
        MaxMp = 100,
        Turns = turns,
    };

    private static EnemyAiContext Ctx(
        BattleFighter enemy,
        BattleFighter? player = null,
        BattleFighter?[]? enemies = null,
        int activeEnemy = 0,
        IReadOnlyCollection<int>? starters = null) => new()
        {
            Enemy = enemy,
            ActivePlayer = player,
            Enemies = enemies ?? [enemy],
            ActiveEnemy = activeEnemy,
            StarterDigimonIds = starters ?? [],
        };

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }

    private sealed class NoRng : IBattleRng
    {
        public int NextValue() => 0;
    }

    private sealed class SeqRng : IBattleRng
    {
        private readonly int[] _values;
        private int _index;

        public SeqRng(params int[] values) => _values = values;

        public int NextValue() => _values[_index++ % _values.Length];
    }

    private sealed class SeqShotRng : IShotCameraRng
    {
        private readonly int[] _values;
        private int _index;

        public SeqShotRng(params int[] values) => _values = values;

        public int NextRoll() => _values[_index++ % _values.Length];
    }

    private sealed record BattleFixture(
        int PlayerAttack = 100,
        int PlayerDefense = 100,
        int PlayerSpeed = 100,
        int PlayerHp = 500,
        int PlayerMp = 100,
        int EnemyRawAttack = 100,
        int EnemyRawDefense = 100,
        int EnemySpeed = 100,
        int EnemyHp = 500,
        int EnemyMp = 100,
        int EnemyStrength = 16);
}
