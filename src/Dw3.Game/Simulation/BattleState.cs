namespace Dw3Production.Simulation;

/// <summary>The player's six battle commands, in the source's order
/// (menu.h BATTLE_COMMAND_*: attack, tech, digivolve, switch, item, run).</summary>
public enum BattleCommand
{
    Attack,
    Technique,
    Digivolve,
    Switch,
    Item,
    Flee,
}

/// <summary>How a battle ends (battle.h BATTLE_FLED/WON/LOST, plus None while running).</summary>
public enum BattleOutcome
{
    None,
    Fled,
    Won,
    Lost,
}

/// <summary>A side, as the turn loop and active-fighter accessors use it.</summary>
public enum BattleSide
{
    Player,
    Enemy,
}

/// <summary>
/// The 1v1 battle state and its turn loop: three fighters per side stored but one active,
/// the element boost, run attempts, and a deterministic time-to-turn scheduler built on
/// DamageCalculator.GetTurnDelay. Engine-free, like WarpController.
///
/// Fidelity vs simplification: the source runs a real-time event queue (FIGHTSTG_events)
/// with per-kind delays and a state machine (WFIGHTMN_states); this core collapses that to
/// a two-actor conditional-turn scheduler whose per-side delay uses the kind-0
/// formula, seeded as the source seeds it (player at 0, enemy at half the player's cycle).
///
/// The six source commands are attack/technique/digivolve/switch/item/run and each is
/// exposed here: attack (first learned skill, FIGHTSTG_startFirstTech), technique (a picked
/// skill, MP spent), digivolve (a permanent change to another of the partner's forms,
/// WFIGHTMN_digivolve — no MP cost, gated only by FIGHTER_NO_DIGIVOLVE), switch (a manual
/// change of the active partner, WFIGHTMN_tag), item (a battle-usable item, FIGHTSTG_startItem)
/// and flee (FIGHTSTG_testRunAway). Knock-outs do NOT auto-switch the player to the next
/// fighter: the battle ends in defeat when no partner is alive, and the switch is the manual
/// Switch command (the Switch and SwitchIn menus). The enemy side decides its turn from its
/// battle table (BattleEnemyAi): attack/technique/switch/run-away,
/// plus the counter-attack its counter field triggers when the player's hit lands. The blast
/// (WFIGHTMN_blast, a temporary gauge-triggered digivolution, not a command) and statuses are
/// out of scope.
/// </summary>
public sealed class BattleState
{
    public BattleFighter?[] Players = new BattleFighter?[3];
    public BattleFighter?[] Enemies = new BattleFighter?[3];

    public int ActivePlayer = -1;
    public int ActiveEnemy = -1;

    public int RunAttempts;
    public int Kind;

    /// <summary>ElementBoost.element; under BattleConst.ElementFirst there is no boost.</summary>
    public int BoostElement;

    /// <summary>ElementBoost.amount, in 128ths.</summary>
    public int BoostAmount;

    /// <summary>BATTLE_SETUP.battle, for the enemy AI's condition 12 (FIGHTSTG_testEnemyCondition).</summary>
    public int BattleId;

    /// <summary>BATTLE_SETUP.encounterKind, for the enemy AI's condition 13.</summary>
    public int EncounterKind;

    /// <summary>The eight partner Digimon ids (DIGIMON_DATA's first eight), for condition 8.</summary>
    public IReadOnlyCollection<int> StarterDigimonIds = [];

    /// <summary>The technique catalogue (id -> resolved technique), for resolving the enemy's
    /// battle-table techniques on its turn and its counter-attack.</summary>
    public IReadOnlyDictionary<int, TechData> Techs = new Dictionary<int, TechData>();

    /// <summary>BATTLE_SETUP.blocks, for the run-away block (and future blocks).</summary>
    public int[] Blocks = new int[12];

    public BattleOutcome Outcome;

    /// <summary>BATTLE_RESULT.partners[i].fought: whether each party member took a turn this
    /// battle. The battle report shares the earned exp only among the partners who took a turn.</summary>
    public bool[] Fought = new bool[3];

    /// <summary>When false (default) criticals never roll, so damage is deterministic.</summary>
    public bool AllowCritical;

    /// <summary>The two sides' time-to-next-turn counters (the conditional-turn scheduler).</summary>
    public int NextPlayerTurn;
    public int NextEnemyTurn;

    public BattleFighter? ActivePlayerFighter => ActivePlayer >= 0 ? Players[ActivePlayer] : null;
    public BattleFighter? ActiveEnemyFighter => ActiveEnemy >= 0 ? Enemies[ActiveEnemy] : null;

    /// <summary>
    /// WFIGHTMN_initFighters: fills the three partners (the first with the active digimon)
    /// and the three enemies, loading HP/MP and resolving stats, then seeds the turn clock.
    /// </summary>
    public static BattleState InitFighters(BattleSetup setup, int digimon)
    {
        var state = new BattleState();
        for (int i = 0; i < setup.Blocks.Length && i < state.Blocks.Length; i++) state.Blocks[i] = setup.Blocks[i];
        state.BattleId = setup.Battle;
        state.EncounterKind = setup.EncounterKind;

        for (int i = 0; i < 3; i++)
        {
            var seed = setup.Partners[i];
            if (seed == null) continue;
            var fighter = new BattleFighter
            {
                Id = i == 0 ? digimon : seed.Id,
                Hp = seed.Hp,
                MaxHp = seed.MaxHp,
                Mp = seed.Mp,
                MaxMp = seed.MaxMp,
                Stats = seed.Stats,
            };
            fighter.Stats.Flags = fighter.Flags;
            fighter.Stats.DamageBonus = fighter.Charge;
            state.Players[i] = fighter;
        }

        for (int i = 0; i < 3; i++)
        {
            var enemy = setup.Enemies[i];
            if (enemy == null || enemy.Fighter == 0) continue;
            var fighter = new BattleFighter
            {
                Id = enemy.Fighter,
                Hp = enemy.Hp,
                MaxHp = enemy.Hp,
                Mp = enemy.Mp,
                MaxMp = enemy.Mp,
                Strength = enemy.Strength,
                Techs = enemy.Techs,
                Actions = enemy.Actions,
                Counter = enemy.Counter,
            };
            fighter.Stats = DamageCalculator.ComputeEnemyStats(enemy.BaseStats, enemy.Level, enemy.Strength);
            fighter.Stats.Flags = fighter.Flags;
            fighter.Stats.DamageBonus = fighter.Charge;
            state.Enemies[i] = fighter;
        }

        state.ActivePlayer = FirstLivingIndex(state.Players);
        state.ActiveEnemy = FirstLivingIndex(state.Enemies);

        state.NextPlayerTurn = 0;
        var activePlayer = state.ActivePlayerFighter;
        var activeEnemy = state.ActiveEnemyFighter;
        if (activePlayer != null && activeEnemy != null)
        {
            int playerDelay = DamageCalculator.GetTurnDelay(
                activePlayer.Stats.Stats[BattleStat.Speed], activeEnemy.Stats.Stats[BattleStat.Speed]);
            state.NextEnemyTurn = playerDelay / 2;
        }
        state.CheckOutcome();
        return state;
    }

    /// <summary>The side whose next-turn time is due first (Player on a tie).</summary>
    public BattleSide NextActor => NextPlayerTurn <= NextEnemyTurn ? BattleSide.Player : BattleSide.Enemy;

    /// <summary>The active partner attacks the active enemy with its basic technique.</summary>
    public bool PlayerAttack(TechData tech, BattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var target = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || target == null) return false;
        int crit = RollCrit(attacker, target, tech, rng);
        int damage = DamageCalculator.ComputeDamage(tech, attacker.Stats, target.Stats, enemyAttacker: false, 0,
            BoostElement, BoostAmount, crit);
        ApplyDamage(target, damage);
        TryEnemyCounter(damage, rng);
        ResolveEnemySlot();
        MarkFought();
        AdvancePlayerTurn();
        CheckOutcome();
        return true;
    }

    /// <summary>The active partner uses a technique (physical or magic by its icon), paying its MP.</summary>
    public bool PlayerTechnique(TechData tech, BattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var target = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || target == null) return false;
        if (tech.Mp > attacker.Mp) return false;    // can't afford it: the turn is not spent
        attacker.Mp -= tech.Mp;
        bool magic = tech.Icon == BattleConst.TechMagic;
        int crit = magic ? RollMagicCrit(target, tech, rng) : RollCrit(attacker, target, tech, rng);
        int damage = magic
            ? DamageCalculator.ComputeMagicDamage(tech, attacker.Stats, target.Stats, false, 0,
                BoostElement, BoostAmount, crit)
            : DamageCalculator.ComputeDamage(tech, attacker.Stats, target.Stats, false, 0,
                BoostElement, BoostAmount, crit);
        ApplyDamage(target, damage);
        TryEnemyCounter(damage, rng);
        ResolveEnemySlot();
        MarkFought();
        AdvancePlayerTurn();
        CheckOutcome();
        return true;
    }

    /// <summary>
    /// The active partner changes into one of its other Digimon forms (the Digivolve
    /// command). It is a permanent change for the battle, costs no MP, and is gated only
    /// by the NoDigivolve flag; picking the current form is refused.
    /// </summary>
    public bool PlayerDigivolve(int form)
    {
        var attacker = ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false }) return false;
        if ((attacker.Flags & FighterFlags.NoDigivolve) != 0) return false;
        if (form == attacker.Id) return false;
        attacker.Id = form;
        MarkFought();
        AdvancePlayerTurn();
        CheckOutcome();
        return true;
    }

    /// <summary>
    /// The manual Switch command: the player
    /// swaps the active partner for another living one. Consumes the turn (the cadence is
    /// re-armed) but does not change any knock-out state, so a knocked-out active can be
    /// replaced without ending the battle.
    /// </summary>
    public bool PlayerSwitch(int slot)
    {
        if (slot < 0 || slot >= Players.Length || slot == ActivePlayer) return false;
        if (Players[slot] is not { Id: not 0, IsKnockedOut: false }) return false;
        ActivePlayer = slot;
        AdvancePlayerTurn();
        return true;
    }

    /// <summary>
    /// The active partner uses a battle item (FIGHTSTG_startItem). The effect is applied to
    /// the active fighter (or the party for revive); the item's removal from the bag is the
    /// the flow controller's concern (the flow controller decrements the bag stock).
    /// </summary>
    public bool PlayerItem(BattleItem item)
    {
        var attacker = ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false }) return false;
        ApplyItemEffect(item);
        MarkFought();
        AdvancePlayerTurn();
        return true;
    }

    /// <summary>
    /// Battle item effects by behaviour key. heal restores a set amount of HP to the active fighter;
    /// revive (0x46) restores all knocked-out partners to full HP; restoreHalfHpMp recovers half HP and MP.
    /// The rest
    /// (cureStatus, boostSpeed/Attack/Defense, special, fillGauge, boostElement, drain,
    /// confuse, weakenDefense, lowerStat, lowerSpeed, itemTech) are not modelled yet and are
    /// consumed without effect — a named simplification.
    /// </summary>
    private void ApplyItemEffect(BattleItem item)
    {
        switch (item.Effect)
        {
            case "heal":
            {
                var fighter = ActivePlayerFighter;
                if (fighter != null) fighter.Hp = Math.Min(fighter.Hp + item.Amount, fighter.MaxHp);
                break;
            }
            case "revive":
                foreach (var player in Players)
                    if (player is { Id: not 0, Hp: 0 }) player.Hp = player.MaxHp;
                break;
            case "restoreHalfHpMp":
            {
                var fighter = ActivePlayerFighter;
                if (fighter != null)
                {
                    fighter.Hp = Math.Min(fighter.Hp + fighter.MaxHp / 2, fighter.MaxHp);
                    fighter.Mp = Math.Min(fighter.Mp + fighter.MaxMp / 2, fighter.MaxMp);
                }
                break;
            }
        }
    }

    /// <summary>The active partner tries to run away (FIGHTSTG_testRunAway). Returns true when it fled.</summary>
    public bool PlayerFlee(BattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var defender = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || defender == null) return false;
        RunAttempts++;
        bool fled = DamageCalculator.TestRunAway(RunAttempts, attacker.Stats, defender.Stats,
            Blocks[BattleConst.BlockRunAway] != 0, attacker.Flags, rng.Next());
        if (fled)
        {
            Outcome = BattleOutcome.Fled;
            return true;
        }
        AdvancePlayerTurn();
        return false;
    }

    /// <summary>The active enemy attacks the active partner with the given technique (its basic
    /// attack on a turn, FIGHTSTG_startEnemyAttack/FIGHTSTG_startFirstTech).</summary>
    public void EnemyAttack(TechData tech, BattleRng rng)
    {
        var attacker = ActiveEnemyFighter;
        var target = ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false }) return;
        int crit = RollCrit(attacker, target, tech, rng);
        int damage = DamageCalculator.ComputeDamage(tech, attacker.Stats, target.Stats, enemyAttacker: true,
            attacker.Strength, BoostElement, BoostAmount, crit);
        ApplyDamage(target, damage);
        EndEnemyTurn();
    }

    /// <summary>The active enemy uses one of its battle-table techniques (useEnemyTech,
    /// the enemy's battle-table techniques): pays the MP and deals the damage. Returns false when it cannot afford the
    /// technique — the source shows a "no MP" message but still spends the turn.</summary>
    public bool EnemyTechnique(TechData tech, BattleRng rng)
    {
        var attacker = ActiveEnemyFighter;
        var target = ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false }) return false;
        if (tech.Mp > attacker.Mp) { EndEnemyTurn(); return false; }
        attacker.Mp -= tech.Mp;
        bool magic = tech.Icon == BattleConst.TechMagic;
        int crit = magic ? RollMagicCrit(target, tech, rng) : RollCrit(attacker, target, tech, rng);
        int damage = magic
            ? DamageCalculator.ComputeMagicDamage(tech, attacker.Stats, target.Stats, true,
                attacker.Strength, BoostElement, BoostAmount, crit)
            : DamageCalculator.ComputeDamage(tech, attacker.Stats, target.Stats, true,
                attacker.Strength, BoostElement, BoostAmount, crit);
        ApplyDamage(target, damage);
        EndEnemyTurn();
        return true;
    }

    /// <summary>The enemy switches to another living fighter (FIGHTSTG_pickEnemySwitch). Returns
    /// the new active slot, or -1 when there is no valid target (the turn is still spent).</summary>
    public int EnemySwitch(EnemyDecision decision, BattleRng rng)
    {
        var attacker = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false }) return -1;
        int slot = BattleEnemyAi.ResolveSwitch(decision, Enemies, ActiveEnemy, rng);
        if (slot >= 0) ActiveEnemy = slot;
        EndEnemyTurn();
        return slot;
    }

    /// <summary>The enemy's run-away action (FIGHTSTG_queueRunAway -> WFIGHTMN_runAway ->
    /// FIGHTSTG_testRunAway): ends the battle as fled on success. Returns whether it fled.</summary>
    public bool EnemyRunAway(BattleRng rng)
    {
        var attacker = ActiveEnemyFighter;
        var defender = ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || defender == null) { EndEnemyTurn(); return false; }
        bool fled = DamageCalculator.TestEnemyRunAway(attacker.Stats, defender.Stats, attacker.Flags, rng.Next());
        if (fled) Outcome = BattleOutcome.Fled;
        else EndEnemyTurn();
        return fled;
    }

    /// <summary>The enemy counter-attacks the damage it just received with the given technique
    /// (counterattack reaction). It is a reaction inside the player's
    /// turn, so it does not spend the enemy's turn.</summary>
    public void EnemyCounter(TechData tech, int receivedDamage, BattleRng rng)
    {
        var attacker = ActiveEnemyFighter;
        var target = ActivePlayerFighter;
        if (attacker is not { IsKnockedOut: false } || target is not { IsKnockedOut: false }) return;
        int crit = RollCrit(attacker, target, tech, rng);
        int damage = DamageCalculator.ComputeCounterDamage(tech, attacker.Stats, target.Stats,
            receivedDamage, BoostElement, BoostAmount, crit);
        ApplyDamage(target, damage);
        CheckOutcome();
    }

    /// <summary>The enemy's turn: decides its action from its battle table
    /// (BattleEnemyAi.PickAction) and executes it. Returns the decision (None when the table is
    /// empty, so the enemy does nothing).</summary>
    public EnemyDecision RunEnemyTurn(BattleRng rng)
    {
        var decision = DecideEnemyAction(rng);
        ExecuteEnemyDecision(decision, rng);
        return decision;
    }

    /// <summary>The enemy's action from its battle table (BattleEnemyAi.PickAction).</summary>
    public EnemyDecision DecideEnemyAction(BattleRng rng)
    {
        var enemy = ActiveEnemyFighter;
        if (enemy is not { IsKnockedOut: false }) return EnemyDecision.None;
        return BattleEnemyAi.PickAction(enemy, BuildEnemyAiContext(), rng);
    }

    private void ExecuteEnemyDecision(EnemyDecision decision, BattleRng rng)
    {
        switch (decision.Kind)
        {
            case EnemyActionKind.Attack:
            {
                var tech = ResolveTech(decision.TechId);
                if (tech != null) EnemyAttack(tech, rng); else EndEnemyTurn();
                break;
            }
            case EnemyActionKind.Technique1:
            case EnemyActionKind.Technique2:
            {
                var tech = ResolveTech(decision.TechId);
                if (tech != null) EnemyTechnique(tech, rng); else EndEnemyTurn();
                break;
            }
            case EnemyActionKind.SwitchTo0:
            case EnemyActionKind.SwitchTo1:
            case EnemyActionKind.SwitchTo2:
            case EnemyActionKind.SwitchToAny:
                EnemySwitch(decision, rng);
                break;
            case EnemyActionKind.RunAway:
                EnemyRunAway(rng);
                break;
            default:
                EndEnemyTurn();
                break;
        }
    }

    /// <summary>After the player's hit lands, checks whether the enemy's counter field triggers
    /// a counter-attack (the enemy's counter field is non-zero).</summary>
    private void TryEnemyCounter(int receivedDamage, BattleRng rng)
    {
        var enemy = ActiveEnemyFighter;
        if (enemy is not { IsKnockedOut: false }) return;
        var decision = BattleEnemyAi.PickCounter(enemy, BuildEnemyAiContext(), rng);
        if (!decision.IsAttack && !decision.IsTechnique) return;
        var tech = ResolveTech(decision.TechId);
        if (tech != null) EnemyCounter(tech, receivedDamage, rng);
    }

    private EnemyAiContext BuildEnemyAiContext() => new()
    {
        Enemy = ActiveEnemyFighter ?? new BattleFighter(),
        ActivePlayer = ActivePlayerFighter,
        Enemies = Enemies,
        ActiveEnemy = ActiveEnemy,
        BoostElement = BoostElement,
        BattleId = BattleId,
        EncounterKind = EncounterKind,
        StarterDigimonIds = StarterDigimonIds,
    };

    private TechData? ResolveTech(int id)
    {
        if (id == 0) return null;
        return Techs.TryGetValue(id, out var tech) ? tech : null;
    }

    /// <summary>Ends the enemy's turn: increments its turns counter, re-arms the cadence and re-checks the outcome.</summary>
    private void EndEnemyTurn()
    {
        var enemy = ActiveEnemyFighter;
        if (enemy != null) enemy.Turns++;
        AdvanceEnemyTurn();
        CheckOutcome();
    }

    private void AdvancePlayerTurn()
    {
        var player = ActivePlayerFighter;
        var enemy = ActiveEnemyFighter;
        if (player == null || enemy == null) return;
        NextPlayerTurn += DamageCalculator.GetTurnDelay(
            player.Stats.Stats[BattleStat.Speed], enemy.Stats.Stats[BattleStat.Speed]);
    }

    private void AdvanceEnemyTurn()
    {
        var player = ActivePlayerFighter;
        var enemy = ActiveEnemyFighter;
        if (player == null || enemy == null) return;
        NextEnemyTurn += DamageCalculator.GetTurnDelay(
            enemy.Stats.Stats[BattleStat.Speed], player.Stats.Stats[BattleStat.Speed]);
    }

    private int RollCrit(BattleFighter attacker, BattleFighter target, TechData tech, BattleRng rng)
    {
        if (!AllowCritical) return -1;
        bool crit = DamageCalculator.RollCritical(tech, attacker.Stats, target.Stats, attacker, target, rng.Next());
        return crit ? rng.Next() & 0x3F : -1;
    }

    private int RollMagicCrit(BattleFighter target, TechData tech, BattleRng rng)
    {
        if (!AllowCritical) return -1;
        bool crit = DamageCalculator.RollMagicCritical(tech, target.Stats, target, rng.Next());
        return crit ? rng.Next() % 65 : -1;
    }

    private static void ApplyDamage(BattleFighter target, int damage)
    {
        target.Hp -= damage;
        if (target.Hp < 0) target.Hp = 0;
    }

    /// <summary>WFIGHTMN_knockOut's enemy branch: the enemy side brings in its next living
    /// fighter when the active one falls to a hit. This reinforcement is distinct from the
    /// enemy's own voluntary switch action (FIGHTSTG_pickEnemySwitch, EnemySwitch).</summary>
    private void ResolveEnemySlot()
    {
        if (ActiveEnemyFighter is { IsKnockedOut: true })
            ActiveEnemy = NextLivingIndex(Enemies, ActiveEnemy);
    }

    private static int FirstLivingIndex(BattleFighter?[] fighters)
    {
        for (int i = 0; i < fighters.Length; i++)
            if (fighters[i] is { Id: not 0, Hp: > 0 }) return i;
        return -1;
    }

    private static int NextLivingIndex(BattleFighter?[] fighters, int current)
    {
        for (int step = 1; step <= fighters.Length; step++)
        {
            int idx = (current + step) % fighters.Length;
            if (fighters[idx] is { Id: not 0, Hp: > 0 }) return idx;
        }
        return -1;
    }

    private void MarkFought()
    {
        if (ActivePlayer >= 0 && ActivePlayer < Fought.Length) Fought[ActivePlayer] = true;
    }

    private void CheckOutcome()
    {
        if (Outcome != BattleOutcome.None) return;
        bool allEnemiesOut = true;
        foreach (var e in Enemies)
        {
            if (e is { Id: not 0, Hp: > 0 }) { allEnemiesOut = false; break; }
        }
        if (allEnemiesOut) { Outcome = BattleOutcome.Won; return; }
        bool allPlayersOut = true;
        foreach (var p in Players)
        {
            if (p is { Id: not 0, Hp: > 0 }) { allPlayersOut = false; break; }
        }
        if (allPlayersOut) Outcome = BattleOutcome.Lost;
    }
}
