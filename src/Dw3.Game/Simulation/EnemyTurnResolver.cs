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

        target.TakeDamage(state.Hits(rng).Basic(tech, attacker, target, enemyAttacker: true));
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
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false })
        {
            return false;
        }

        if (tech.Mp > attacker.Mp)
        {
            EndTurn();
            return false;
        }

        attacker.Mp -= tech.Mp;
        target.TakeDamage(state.Hits(rng).Technique(tech, attacker, target, enemyAttacker: true));
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
            state.Outcome = BattleOutcome.Fled;
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

        target.TakeDamage(state.Hits(rng).Counter(tech, attacker, target, receivedDamage));
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
        var tech = decision.IsAttack || decision.IsTechnique ? ResolveTech(decision.TechId) : null;
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

        var tech = decision.IsAttack || decision.IsTechnique ? ResolveTech(decision.TechId) : null;
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

    /// <summary>Looks a battle-table technique up in the catalogue; id 0 is an empty table slot.</summary>
    /// <param name="id">The technique id from the table.</param>
    /// <returns>The technique, or <see langword="null"/> for id 0 or an unknown id.</returns>
    private TechData? ResolveTech(int id)
    {
        if (id == 0)
        {
            return null;
        }

        return state.Techs.TryGetValue(id, out var tech) ? tech : null;
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
