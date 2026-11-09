namespace Dw3Production.Simulation;

/// <summary>
/// The enemy side's actions in a <see cref="BattleState"/>: its turn decided from its battle
/// table (attack, technique, switch or run away) and the counter-attack its counter row can
/// trigger inside the player's turn. Every action that spends the enemy's turn ends with
/// <see cref="EndTurn"/>, which counts the turn for the table's turn conditions, re-arms the
/// enemy's cadence and re-checks the outcome.
/// </summary>
/// <param name="state">The battle the enemy acts in.</param>
public sealed class EnemyTurnResolver(BattleState state)
{
    /// <summary>Decides the enemy's action from its battle table and executes it.</summary>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>The decision, <see cref="EnemyDecision.None"/> when the enemy cannot act.</returns>
    public EnemyDecision RunTurn(IBattleRng rng)
    {
        var enemy = state.ActiveEnemyFighter;
        if (enemy is not { IsKnockedOut: false })
        {
            return EnemyDecision.None;
        }

        if ((enemy.Flags & FighterFlags.Asleep) != 0)
        {
            EndTurn();
            return EnemyDecision.None;
        }

        if (state.TryLoseEnemyTurnToParalysis(rng))
        {
            return EnemyDecision.None;
        }

        var decision = Decide(rng);
        Execute(decision, rng);
        return decision;
    }

    /// <summary>Picks the active enemy's action from its battle table without executing it.</summary>
    /// <param name="rng">The battle's random source, consumed by random conditions.</param>
    /// <returns>The decision, <see cref="EnemyDecision.None"/> when the active enemy is down or absent.</returns>
    public EnemyDecision Decide(IBattleRng rng)
    {
        var enemy = state.ActiveEnemyFighter;
        if (enemy is not { IsKnockedOut: false })
        {
            return EnemyDecision.None;
        }

        return BattleEnemyAi.PickAction(enemy, BuildContext(), rng);
    }

    /// <summary>The active enemy hits the active partner with its basic technique, then ends its turn.</summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="rng">The battle's random source.</param>
    public void Attack(TechData tech, IBattleRng rng)
    {
        var attacker = state.ActiveEnemyFighter;
        var target = state.ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false })
        {
            return;
        }

        if ((attacker.Flags & FighterFlags.Asleep) != 0)
        {
            EndTurn();
            return;
        }

        int damage = state.ResolveHitSequence(BattleSide.Enemy, tech, attacker.Stats, target, false, _ => state.Hits(rng).Basic(tech, attacker, target, enemyAttacker: true));
        WakePlayerAfterHits(damage, rng);

        EndTurn();
    }

    /// <summary>
    /// The active enemy uses one of its battle-table techniques, paying its MP. An unaffordable
    /// technique still spends the turn, as the disc's "no MP" message does.
    /// </summary>
    /// <param name="tech">The technique used.</param>
    /// <param name="rng">The battle's random source.</param>
    /// <returns><see langword="false"/> when the enemy could not act or afford the technique.</returns>
    public bool Technique(TechData tech, IBattleRng rng)
    {
        var attacker = state.ActiveEnemyFighter;
        var target = state.ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false } || !BattleTechniqueEffectResolver.CanExecute(BattleSide.Enemy, tech) || !DoubleMagicTechniqueSelector.HasRequiredDamageEntries(tech, state.Techs))
        {
            return false;
        }

        if ((attacker.Flags & FighterFlags.Asleep) != 0)
        {
            EndTurn();
            return false;
        }

        if (tech.Mp > attacker.Mp)
        {
            EndTurn();
            return false;
        }

        attacker.Mp -= tech.Mp;
        if (!BattleTechniqueEffectResolver.RequiresHitSequence(tech))
        {
            state.ApplyTechniqueEffect(BattleSide.Enemy, tech, rng, 0);
            state.ConsumeCharge(BattleSide.Enemy);
            EndTurn();
            return true;
        }

        int damage = state.ResolveHitSequence(BattleSide.Enemy, tech, attacker.Stats, target, false, hitIndex => state.Hits(rng).Technique(tech, DoubleMagicTechniqueSelector.DamageTechniqueForHit(tech, state.Techs, hitIndex), attacker, target, enemyAttacker: true));
        WakePlayerAfterHits(damage, rng);
        ApplyEnemyEffectAfterHits(tech, target, state.LastResolvedDamage, rng);

        state.ConsumeCharge(BattleSide.Enemy);
        EndTurn();
        return true;
    }

    /// <summary>The enemy brings in another living fighter; the turn is spent even without a valid target.</summary>
    /// <param name="decision">The switch decision, naming a slot or any slot.</param>
    /// <param name="rng">The battle's random source, consumed when two slots qualify.</param>
    /// <returns>The new active slot, or -1.</returns>
    public int Switch(EnemyDecision decision, IBattleRng rng)
    {
        if (state.ActiveEnemyFighter is not { IsKnockedOut: false })
        {
            return -1;
        }

        int slot = BattleEnemyAi.ResolveSwitch(decision, state.Enemies, state.ActiveEnemy, rng);
        if (slot >= 0)
        {
            state.ActiveEnemy = slot;
        }

        EndTurn();
        return slot;
    }

    /// <summary>The enemy tries to run away; success ends the battle as fled, failure spends its turn.</summary>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>Whether the enemy fled.</returns>
    public bool RunAway(IBattleRng rng)
    {
        var attacker = state.ActiveEnemyFighter;
        var defender = state.ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || defender == null)
        {
            EndTurn();
            return false;
        }

        bool fled = RunAwayRules.TestEnemyRunAway(attacker.Stats, defender.Stats, attacker.Flags, rng.NextValue());
        if (fled)
        {
            state.Outcome = BattleOutcome.EnemyFled;
        }
        else
        {
            attacker.Turns++;
            state.AdvanceEnemyRunAway(rng);
            state.CheckOutcome();
        }

        return fled;
    }

    /// <summary>
    /// The enemy counter-attacks the damage it just received. It is a reaction inside the
    /// player's turn, so it does not spend the enemy's turn.
    /// </summary>
    /// <param name="tech">The counter technique.</param>
    /// <param name="receivedDamage">The damage of the hit that triggered it.</param>
    /// <param name="rng">The battle's random source.</param>
    public void Counter(TechData tech, int receivedDamage, IBattleRng rng)
    {
        var attacker = state.ActiveEnemyFighter;
        var target = state.ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false })
        {
            return;
        }

        int damage = state.ResolveHitSequence(BattleSide.Enemy, tech, attacker.Stats, target, true, _ => state.Hits(rng).Counter(tech, attacker, target, receivedDamage));
        WakePlayerAfterHits(damage, rng);
        state.ConsumeCharge(BattleSide.Enemy);

        state.CheckOutcome();
    }

    /// <summary>
    /// After the player's hit lands, fires the enemy's counter row when its condition holds and
    /// it names an attack or a technique the catalogue knows.
    /// </summary>
    /// <param name="receivedDamage">The damage of the player's hit.</param>
    /// <param name="rng">The battle's random source.</param>
    public void TryCounter(int receivedDamage, IBattleRng rng)
    {
        var enemy = state.ActiveEnemyFighter;
        if (enemy is not { IsKnockedOut: false })
        {
            return;
        }

        var decision = BattleEnemyAi.PickCounter(enemy, BuildContext(), rng);
        var tech = ResolveTech(decision);
        if (tech != null)
        {
            Counter(tech, receivedDamage, rng);
        }
    }

    /// <summary>Executes a decision; an attack or technique whose id the catalogue lacks only spends the turn.</summary>
    /// <param name="decision">The decision picked from the battle table.</param>
    /// <param name="rng">The battle's random source.</param>
    private void Execute(EnemyDecision decision, IBattleRng rng)
    {
        if (decision.IsSwitch)
        {
            Switch(decision, rng);
            return;
        }

        var tech = ResolveTech(decision);
        switch (decision.Kind)
        {
            case EnemyActionKind.Attack when tech != null:
                Attack(tech, rng);
                break;
            case EnemyActionKind.Technique1 or EnemyActionKind.Technique2 when tech != null:
                Technique(tech, rng);
                break;
            case EnemyActionKind.RunAway:
                RunAway(rng);
                break;
            default:
                EndTurn();
                break;
        }
    }

    /// <summary>Snapshots the battle values the enemy's table conditions read.</summary>
    /// <returns>A context for the active enemy (an empty fighter when there is none).</returns>
    private EnemyAiContext BuildContext() => new()
    {
        Enemy = state.ActiveEnemyFighter ?? new BattleFighter(),
        ActivePlayer = state.ActivePlayerFighter,
        Enemies = state.Enemies,
        ActiveEnemy = state.ActiveEnemy,
        BoostElement = state.Boost.Element,
        BattleId = state.BattleId,
        EncounterKind = state.EncounterKind,
        StarterDigimonIds = state.StarterDigimonIds,
    };

    /// <summary>Looks an executable action's technique up in the catalogue.</summary>
    /// <param name="decision">Action-table decision that may name a basic attack or technique.</param>
    /// <returns>The technique, or <see langword="null"/> when the decision has no technique.</returns>
    /// <exception cref="InvalidOperationException">The action names a technique absent from the loaded catalogue.</exception>
    private TechData? ResolveTech(EnemyDecision decision)
    {
        if (!decision.IsAttack && !decision.IsTechnique)
        {
            return null;
        }

        return state.Techs.TryGetValue(decision.TechId, out var tech)
            ? tech
            : throw new InvalidOperationException($"enemy-ai-technique-not-found action={decision.Kind} technique={decision.TechId}");
    }

    /// <summary>Wakes the active player once after a sequence that actually dealt damage.</summary>
    /// <param name="damage">Total HP removed by the sequence.</param>
    /// <param name="rng">Shared battle random source.</param>
    private void WakePlayerAfterHits(int damage, IBattleRng rng)
    {
        if (damage > 0)
        {
            _ = state.TryWakeOnHit(BattleSide.Player, damage, rng);
        }
    }

    /// <summary>Applies one supported non-multi technique effect after the enemy's landed sequence.</summary>
    /// <param name="tech">Technique whose effect may apply.</param>
    /// <param name="target">Original player target of the valid hit sequence.</param>
    /// <param name="damage">Aggregate damage landed by the sequence.</param>
    /// <param name="rng">Shared battle random source.</param>
    private void ApplyEnemyEffectAfterHits(TechData tech, BattleFighter target, int damage, IBattleRng rng)
    {
        if (state.AnyActionHitLanded && tech.Effect != BattleTechniqueEffect.MultiHit)
        {
            state.ApplyTechniqueEffect(BattleSide.Enemy, tech, rng, damage, target);
        }
    }

    /// <summary>Counts the turn on the active enemy, re-arms the enemy cadence and re-checks the outcome.</summary>
    private void EndTurn()
    {
        var enemy = state.ActiveEnemyFighter;
        if (enemy != null)
        {
            enemy.Turns++;
        }

        state.AdvanceEnemyTurn();
        state.CheckOutcome();
    }
}
