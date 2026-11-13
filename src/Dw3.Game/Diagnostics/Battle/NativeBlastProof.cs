using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises the automatic Blast through ordinary native turns: a root Attack through GameHost input, then the
/// host's scheduler-owned enemy action, chosen by the production enemy AI from a one-row Attack table, whose received damage charges the gauge. Gauge, catalogue, table and stats are SYNTHETIC
/// preconditions mirroring the qualified BattleBlastTests vector. Trigger, form change and return are never forced.</summary>
internal static class NativeBlastProof
{
    /// <summary>SYNTHETIC temporary Blast form id carried by the partner's prepared configuration.</summary>
    private const int BlastFormId = 5;

    /// <summary>Synthetic origin form of the active partner.</summary>
    private const int BaseFormId = 1;

    /// <summary>Synthetic partner maximum HP, as in the qualified gauge vector.</summary>
    private const int MaxHp = 300;

    /// <summary>SYNTHETIC enemy table technique id resolved by the production enemy AI for its one Attack row.</summary>
    private const int EnemyAttackTechId = 0x90;

    /// <summary>Upper bound of ordinary steps for the temporary form to return.</summary>
    private const int ReturnSteps = 200;

    /// <summary>Runs the below-threshold control, then the threshold trigger, single application and return.</summary>
    public static void Run()
    {
        BelowThreshold();
        ThresholdTriggerAndReturn();
    }

    /// <summary>Gauge 0 plus one received enemy hit charges the gauge without a form change.</summary>
    private static void BelowThreshold()
    {
        using var session = Create(gauge: 0);
        BattleFighter player = session.Player;
        int hp = ReceiveFirstEnemyHit(session);
        ProbeRequirement.Require(player.BlastGauge is > 0 and < BattleBlastGaugeRules.GaugeThreshold, $"below-threshold gauge did not charge gauge={player.BlastGauge}");
        ProbeRequirement.Require(player.Id == BaseFormId && !player.Temporary && player.Hp < hp, $"below-threshold changed form id={player.Id} temporary={player.Temporary} hp={hp}->{player.Hp}");
        GD.Print($"NATIVE_BLAST PASS case=below-threshold fixture=SYNTHETIC gauge=0->{player.BlastGauge} hp={hp}->{player.Hp} form={player.Id} temporary=False");
    }

    /// <summary>Gauge 999 plus one received enemy hit crosses the threshold, transforms once and returns by the scheduler.</summary>
    private static void ThresholdTriggerAndReturn()
    {
        using var session = Create(gauge: 999);
        BattleFighter player = session.Player;
        int from = session.Rng.Count;
        int hp = ReceiveFirstEnemyHit(session);
        ProbeRequirement.Require(player.Temporary && player.Id == BlastFormId && player.PrevId == BaseFormId, $"Blast did not trigger id={player.Id} prev={player.PrevId} temporary={player.Temporary}");
        ProbeRequirement.Require(player.BlastGauge == BattleBlastGaugeRules.GaugeThreshold && player.Hp == MaxHp, $"Blast resources wrong gauge={player.BlastGauge} hp={player.Hp}");
        GD.Print($"NATIVE_BLAST PASS case=trigger fixture=SYNTHETIC gauge=999->{player.BlastGauge} form={BaseFormId}->{player.Id} prev={player.PrevId} hp-before-hit={hp} hp={player.Hp}/{MaxHp} phase={session.Flow.Phase} rng={session.Rng.DrawsSince(from)}");
        int mp = player.Mp;
        UseTechniqueWhileTemporary(session);
        ProbeRequirement.Require(player.Mp == mp, $"temporary form technique was not free mp={mp}->{player.Mp}");
        int steps = RunTemporaryPeriod(session, out int hits, out int lowestHp);
        ProbeRequirement.Require(hits > 0 && lowestHp < MaxHp, $"no incoming damage observed while temporary hits={hits} lowest={lowestHp}");
        ProbeRequirement.Require(player.Id == BaseFormId && !player.Temporary && player.BlastGauge == 0, $"Blast did not return id={player.Id} gauge={player.BlastGauge}");
        GD.Print($"NATIVE_BLAST PASS case=single-application-and-return temporary-enemy-hits={hits} lowest-hp={lowestHp} prev-stable=True technique-mp={mp}->{player.Mp} returned-after-steps={steps} form={BlastFormId}->{player.Id} gauge=1000->{player.BlastGauge}");
    }

    /// <summary>Builds the SYNTHETIC session with the qualified gauge vector stats and catalogue.</summary>
    /// <param name="gauge">Gauge carried by the partner seed before the battle starts.</param>
    /// <returns>Started native session at the player's command phase.</returns>
    private static NativeMenuSession Create(int gauge)
    {
        var session = new NativeMenuSession(fixture: new(setup =>
        {
            PartnerSeed seed = NativeMenuFixture.Partner(setup);
            seed.Hp = MaxHp;
            seed.MaxHp = MaxHp;
            seed.BlastGauge = gauge;
            seed.Blast = new BattleBlastConfiguration(BlastFormId, 0, []);
            seed.Stats.Stats[BattleStat.Attack] = 20;
            seed.Stats.Stats[BattleStat.Defense] = 1;
            NativeMenuFixture.Enemy(setup).Techs = [EnemyAttackTechId, 0, 0];
            NativeMenuFixture.Enemy(setup).Actions = [new BattleTableAction(1, 0, 0)];
        }));
        var techs = new Dictionary<int, TechData>(session.Flow.Techs) { [EnemyAttackTechId] = new TechData { Accuracy = 128, Power = 22, HitCount = 1 } };
        session.Flow.Techs = techs;
        session.Flow.State.Techs = techs;
        return session;
    }

    /// <summary>Performs one root Attack through host input, then advances to and through the first enemy action.</summary>
    /// <param name="session">Native session at the player's command phase.</param>
    /// <returns>Partner HP immediately before the enemy action.</returns>
    private static int ReceiveFirstEnemyHit(NativeMenuSession session)
    {
        int draws = session.Rng.Count;
        NativeTurnDriver.Step(session);
        ProbeRequirement.Require(session.Rng.Count > draws && session.Flow.Phase != BattleFlowPhase.PlayerCommand, "normal Attack command was not resolved through host input");
        NativeTurnDriver.AdvanceUntil(session, () => session.Flow.Phase == BattleFlowPhase.EnemyTurn, 16);
        int hp = session.Player.Hp;
        session.EnemyAct();
        return hp;
    }

    /// <summary>Confirms a SYNTHETIC 5 MP scripted technique through host input while the partner is temporary.</summary>
    /// <param name="session">Native session after the triggering enemy action.</param>
    private static void UseTechniqueWhileTemporary(NativeMenuSession session)
    {
        NativeTurnDriver.AdvanceUntil(session, () => session.Flow.Phase == BattleFlowPhase.PlayerCommand, 8);
        session.Flow.PlayerTechniques = [new BattleTechnique(7, "synthetic", new TechData { Mp = 5, Accuracy = 128, Power = 1000, HitCount = 1, Script = 5 })];
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        int enemyHp = session.Enemy.Hp;
        NativeMenuCommandsProof.Select(session, 1);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && session.Menu.CanConfirm, "technique page unavailable while temporary");
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.ActionSequence && session.Enemy.Hp < enemyHp, $"temporary technique did not resolve phase={session.Flow.Phase} enemy-hp={enemyHp}->{session.Enemy.Hp}");
        session.Flow.CompleteActionSequence();
    }

    /// <summary>Advances ordinary turns while temporary, requiring no second trigger, until the scheduled return.</summary>
    /// <param name="session">Native session.</param>
    /// <param name="hits">Enemy actions observed while temporary.</param>
    /// <param name="lowestHp">Lowest partner HP observed while temporary.</param>
    /// <returns>Steps consumed until the return.</returns>
    private static int RunTemporaryPeriod(NativeMenuSession session, out int hits, out int lowestHp)
    {
        BattleFighter player = session.Player;
        hits = 0;
        lowestHp = player.Hp;
        for (int step = 0; step < ReturnSteps; step++)
        {
            if (!player.Temporary)
            {
                return step;
            }

            hits += session.Flow.Phase == BattleFlowPhase.EnemyTurn ? 1 : 0;
            NativeTurnDriver.Step(session);
            lowestHp = Math.Min(lowestHp, player.Hp);
            ProbeRequirement.Require(!player.Temporary || (player.Id == BlastFormId && player.PrevId == BaseFormId && player.BlastGauge == BattleBlastGaugeRules.GaugeThreshold), $"duplicate Blast or identity drift id={player.Id} prev={player.PrevId} gauge={player.BlastGauge}");
        }

        throw new InvalidOperationException($"Blast did not return within {ReturnSteps} steps");
    }
}
