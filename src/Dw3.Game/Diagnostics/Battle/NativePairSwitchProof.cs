using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;
using BattleEnemy = Dw3Production.Simulation.BattleEnemy;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Proves voluntary switching, forced replacement and pair actions through GameHost input and callbacks.</summary>
internal static class NativePairSwitchProof
{
    /// <summary>Executes an ordinary voluntary change through all three production menu pages.</summary>
    public static void VoluntarySwitch()
    {
        using var session = new NativeMenuSession(BuildFlow(), EmptyText(), EmptyItems());

        OpenPairChoice(session);
        ProbeRequirement.Require(session.Menu.Cursor == 0 && session.Menu.CanConfirm, "ordinary switch row is unavailable");
        session.Tap(Key.Enter);

        ProbeRequirement.Require(
            session.Flow.State.ActivePlayer == 1 && session.Flow.State.ActivePlayerFighter?.Id == 20,
            "voluntary switch did not activate the selected reserve");
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.EnemyTurn, "voluntary switch did not spend one turn");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS switch=voluntary active=20 input=host-three-pages");
    }

    /// <summary>Executes a source-qualified pair action through the selectable pair row and verifies both costs and the incoming active partner.</summary>
    public static void PairAction()
    {
        using var session = new NativeMenuSession(BuildFlow(pairNameId: 99), EmptyText(), EmptyItems());
        BattleFighter outgoing = session.Player;
        BattleFighter incoming = session.Flow.State.Players[1] ?? throw new InvalidOperationException("pair reserve missing");
        int targetHp = session.Enemy.Hp;
        int outgoingMp = outgoing.Mp;
        int incomingMp = incoming.Mp;

        OpenPairChoice(session);
        session.Tap(Key.Down);
        ProbeRequirement.Require(session.Menu.Cursor == 1 && session.Menu.CanConfirm, "qualified pair row is unavailable");
        session.Tap(Key.Enter);

        ProbeRequirement.Require(
            outgoing.Mp == outgoingMp - 7 && incoming.Mp == incomingMp - 7 && session.Enemy.Hp < targetHp,
            "pair did not debit both partners exactly once and damage the target");
        ProbeRequirement.Require(
            session.Flow.State.ActivePlayer == 1 && session.Flow.State.ActivePlayerFighter?.Id == 20 && session.Flow.State.Fought[1],
            "pair did not activate and credit the incoming partner");
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.EnemyTurn, "pair did not spend one turn");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS pair=qualified active=20 mp=both-once target=damaged input=host-three-pages");
    }

    /// <summary>Checks that invalid relations and insufficient MP remain disabled without consuming state or RNG.</summary>
    public static void PairRefusals()
    {
        RefusePairBeforeMutation(BuildFlow(pairNameId: 98), "partner-name");
        BattleFlowController insufficientMp = BuildFlow(pairNameId: 99);
        BattleFighter reserve = insufficientMp.State.Players[1] ?? throw new InvalidOperationException("pair reserve missing");
        reserve.Mp = 6;
        RefusePairBeforeMutation(insufficientMp, "mp");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS pair-refusal=partner-name-and-mp state-rng=unchanged");
    }

    /// <summary>Forces a real enemy knockout, then proves cancellation cannot escape the mandatory replacement path.</summary>
    public static void ForcedReplacement()
    {
        BattleFlowController flow = BuildFlow(enemyFirst: true);
        BattleFighter outgoing = flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("forced outgoing missing");
        outgoing.Hp = 1;
        flow.EnemyBasicTech = new TechData { Accuracy = 128, Power = 10000 };
        using var session = new NativeMenuSession(flow, EmptyText(), EmptyItems());

        session.Flow.EnemyAct(session.Rng);
        ProbeRequirement.Require(session.Player.IsKnockedOut && session.Flow.Phase == BattleFlowPhase.PlayerCommand, "enemy did not produce an active-player knockout with a reserve");
        session.Tick();
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Switch, "knockout did not open the forced switch page");

        session.Tap(Key.X);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Switch, "forced switch cancellation escaped to an unplayable root");
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.SwitchIn, "forced switch did not open the reserve form page");
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.PairSwitch, "forced switch did not open pair choice page");
        session.Tap(Key.X);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.SwitchIn, "pair cancellation did not retain forced-menu depth");
        session.Tap(Key.X);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Switch, "form cancellation did not retain forced-menu depth");
        session.Tap(Key.Enter);
        session.Tap(Key.Enter);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(
            session.Flow.State.ActivePlayer == 1 && session.Flow.State.ActivePlayerFighter?.Id == 20 && session.Flow.Phase == BattleFlowPhase.EnemyTurn,
            "forced replacement did not activate the living reserve through host input");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS switch=forced ko=enemy-action cancel=depth-preserved active=20");
    }

    /// <summary>Opens the source menu path root Change to reserve form to ordinary-or-pair choice.</summary>
    /// <param name="session">Native production-host fixture.</param>
    private static void OpenPairChoice(NativeMenuSession session)
    {
        NativeMenuCommandsProof.Select(session, 3);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Switch, "root Change did not open partner selection");
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.SwitchIn, "partner selection did not open form selection");
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.PairSwitch, "form selection did not open pair choice");
    }

    /// <summary>Opens a pair choice and verifies its pair row is rejected before any simulation mutation.</summary>
    /// <param name="flow">Configured fabricated flow consumed by the unchanged production host.</param>
    /// <param name="reason">Stable label for the deliberate rejection condition.</param>
    private static void RefusePairBeforeMutation(BattleFlowController flow, string reason)
    {
        using var session = new NativeMenuSession(flow, EmptyText(), EmptyItems());
        OpenPairChoice(session);
        session.Tap(Key.Down);
        ProbeRequirement.Require(session.Menu.Cursor == 1 && !session.Menu.CanConfirm, $"pair refusal row must be disabled reason={reason}");
        string before = NativeMenuCommandsProof.Snapshot(session);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(
            session.Menu.Menu == BattleMenuKind.PairSwitch && NativeMenuCommandsProof.Snapshot(session) == before,
            $"pair refusal mutated state or RNG reason={reason}");
    }

    /// <summary>Builds a deterministic two-partner flow with an optional source-qualified pair relation.</summary>
    /// <param name="pairNameId">Incoming name id required by the relation, or zero for no relation.</param>
    /// <param name="enemyFirst">Whether the enemy starts, for the controlled knockout path.</param>
    /// <returns>Initialized production flow with fabricated data only.</returns>
    private static BattleFlowController BuildFlow(int pairNameId = 0, bool enemyFirst = false)
    {
        var setup = new BattleSetup { AmbushChance = enemyFirst ? 128 : 0 };
        setup.Partners[0] = Seed(10, enemyFirst ? 1 : 100, 50, enemyFirst ? 1 : 999);
        setup.Partners[1] = Seed(20, 100, 50, enemyFirst ? 1 : 999);
        setup.Enemies[0] = new BattleEnemy
        {
            Fighter = 30,
            Hp = 100000,
            Mp = 0,
            Strength = 16,
            BaseStats = Stats(enemyFirst ? 999 : 1),
            Techs = [42, 0, 0],
            Actions = [new BattleTableAction(1, 0, 0)],
        };
        var pairTechnique = new TechData { Accuracy = 128, Power = 50, Mp = 7 };
        var flow = new BattleFlowController
        {
            EnemyBasicTech = new TechData { Accuracy = 128, Power = 1 },
            Techs = new Dictionary<int, TechData> { [42] = pairTechnique },
            NameIds = new Dictionary<int, int> { [10] = 10, [20] = 99, [30] = 30 },
            PairTechniquesByForm = pairNameId > 0
                ? new Dictionary<int, PairTechniqueDefinition> { [10] = new(42, pairNameId) }
                : [],
        };
        flow.Start(BattleState.InitFighters(setup, 10, new BattleAttackProofRng(0)));
        return flow;
    }

    /// <summary>Creates one fabricated living partner with controlled resources and speed.</summary>
    /// <param name="id">Fighter id assigned to the partner.</param>
    /// <param name="hp">Current and maximum hit points.</param>
    /// <param name="mp">Current and maximum magic points.</param>
    /// <param name="speed">Turn-order statistic.</param>
    /// <returns>Fabricated partner seed.</returns>
    private static PartnerSeed Seed(int id, int hp, int mp, int speed) => new()
    {
        Id = id,
        Hp = hp,
        MaxHp = hp,
        Mp = mp,
        MaxMp = mp,
        Stats = Stats(speed),
    };

    /// <summary>Builds deterministic battle statistics for the fabricated fixture.</summary>
    /// <param name="speed">Turn-order statistic.</param>
    /// <returns>Statistics with the requested speed.</returns>
    private static BattleStats Stats(int speed)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = 100;
        stats.Stats[BattleStat.Defense] = 1;
        stats.Stats[BattleStat.Speed] = speed;
        return stats;
    }

    /// <summary>Builds the minimum text table accepted by the native host constructor.</summary>
    /// <returns>Empty fabricated text data.</returns>
    private static BattleTextData EmptyText() => new([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>());

    /// <summary>Builds the minimum item table accepted by the native host constructor.</summary>
    /// <returns>Empty fabricated item data.</returns>
    private static BattleItemsData EmptyItems() => new([]);
}
