namespace Dw3Production.Simulation;

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
/// battle table (BattleEnemyAi, through EnemyTurnResolver): attack/technique/switch/run-away,
/// plus the counter-attack its counter field triggers when the player's hit lands. The blast
/// (WFIGHTMN_blast, a temporary gauge-triggered digivolution, not a command) and statuses are
/// out of scope.
/// </summary>
public sealed class BattleState
{
    private readonly EnemyTurnResolver _enemyTurn;

    /// <summary>Initializes a new instance of the <see cref="BattleState"/> class with empty sides and no outcome.</summary>
    public BattleState()
    {
        _enemyTurn = new EnemyTurnResolver(this);
    }

    public BattleFighter?[] Players { get; set; } = new BattleFighter?[3];

    public BattleFighter?[] Enemies { get; set; } = new BattleFighter?[3];

    public int ActivePlayer { get; set; } = -1;

    public int ActiveEnemy { get; set; } = -1;

    public int RunAttempts { get; set; }

    public int Kind { get; set; }

    /// <summary>Gets or sets the battlefield's element boost (none by default).</summary>
    public ElementBoost Boost { get; set; }

    /// <summary>Gets or sets BATTLE_SETUP.battle, for the enemy AI's condition 12 (FIGHTSTG_testEnemyCondition).</summary>
    public int BattleId { get; set; }

    /// <summary>Gets or sets BATTLE_SETUP.encounterKind, for the enemy AI's condition 13.</summary>
    public int EncounterKind { get; set; }

    /// <summary>Gets or sets the eight partner Digimon ids (DIGIMON_DATA's first eight), for condition 8.</summary>
    public IReadOnlyCollection<int> StarterDigimonIds { get; set; } = [];

    /// <summary>Gets or sets the technique catalogue (id -> resolved technique), for resolving the enemy's
    /// battle-table techniques on its turn and its counter-attack.</summary>
    public IReadOnlyDictionary<int, TechData> Techs { get; set; } = new Dictionary<int, TechData>();

    /// <summary>Gets or sets BATTLE_SETUP.blocks, for the run-away block (and future blocks).</summary>
    public int[] Blocks { get; set; } = new int[12];

    public BattleOutcome Outcome { get; set; }

    /// <summary>Gets or sets BATTLE_RESULT.partners[i].fought: whether each party member took a turn this
    /// battle. The battle report shares the earned exp only among the partners who took a turn.</summary>
    public bool[] Fought { get; set; } = new bool[3];

    /// <summary>Gets or sets a value indicating whether criticals roll; when false (default) damage is deterministic.</summary>
    public bool AllowCritical { get; set; }

    /// <summary>Gets or sets the player side's time-to-next-turn counter (the conditional-turn scheduler).</summary>
    public int NextPlayerTurn { get; set; }

    /// <summary>Gets or sets the enemy side's time-to-next-turn counter.</summary>
    public int NextEnemyTurn { get; set; }

    public BattleFighter? ActivePlayerFighter => ActivePlayer >= 0 ? Players[ActivePlayer] : null;

    public BattleFighter? ActiveEnemyFighter => ActiveEnemy >= 0 ? Enemies[ActiveEnemy] : null;

    /// <summary>Gets the side whose next-turn time is due first (Player on a tie).</summary>
    public BattleSide NextActor => NextPlayerTurn <= NextEnemyTurn ? BattleSide.Player : BattleSide.Enemy;

    /// <summary>
    /// WFIGHTMN_initFighters: fills the three partners (the first with the active digimon)
    /// and the three enemies, loading HP/MP and resolving stats, then seeds the turn clock.
    /// </summary>
    public static BattleState InitFighters(BattleSetup setup, int digimon)
    {
        var state = new BattleState
        {
            BattleId = setup.Battle,
            EncounterKind = setup.EncounterKind,
        };
        Array.Copy(setup.Blocks, state.Blocks, Math.Min(setup.Blocks.Length, state.Blocks.Length));

        for (int i = 0; i < 3; i++)
        {
            var seed = setup.Partners[i];
            state.Players[i] = seed == null ? null : BattleFighter.FromPartner(seed, i == 0 ? digimon : seed.Id);
            var enemy = setup.Enemies[i];
            state.Enemies[i] = enemy is null or { Fighter: 0 } ? null : BattleFighter.FromEnemy(enemy);
        }

        state.ActivePlayer = BattleRoster.FirstLivingIndex(state.Players);
        state.ActiveEnemy = BattleRoster.FirstLivingIndex(state.Enemies);
        state.SeedTurnClock();
        state.CheckOutcome();
        return state;
    }

    /// <summary>The active partner attacks the active enemy with its basic technique.</summary>
    public bool PlayerAttack(TechData tech, IBattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var target = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || target == null)
        {
            return false;
        }

        LandPlayerHit(target, Hits(rng).Basic(tech, attacker, target, enemyAttacker: false), rng);
        return true;
    }

    /// <summary>The active partner uses a technique (physical or magic by its icon), paying its MP.</summary>
    public bool PlayerTechnique(TechData tech, IBattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var target = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || target == null || tech.Mp > attacker.Mp)
        {
            return false; // an unaffordable technique does not spend the turn
        }

        attacker.Mp -= tech.Mp;
        LandPlayerHit(target, Hits(rng).Technique(tech, attacker, target, enemyAttacker: false), rng);
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
        if (attacker is not { IsKnockedOut: false } || (attacker.Flags & FighterFlags.NoDigivolve) != 0 || form == attacker.Id)
        {
            return false;
        }

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
        if (slot < 0 || slot >= Players.Length || slot == ActivePlayer || Players[slot] is not { Id: not 0, IsKnockedOut: false })
        {
            return false;
        }

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
        if (attacker is not { IsKnockedOut: false })
        {
            return false;
        }

        BattleItemEffects.Apply(item, attacker, Players);
        MarkFought();
        AdvancePlayerTurn();
        return true;
    }

    /// <summary>The active partner tries to run away (FIGHTSTG_testRunAway). Returns true when it fled.</summary>
    public bool PlayerFlee(IBattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var defender = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || defender == null)
        {
            return false;
        }

        RunAttempts++;
        bool fled = RunAwayRules.TestRunAway(RunAttempts, attacker.Stats, defender.Stats,
            Blocks[BattleConst.BlockRunAway] != 0, attacker.Flags, rng.NextValue());
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
    public void EnemyAttack(TechData tech, IBattleRng rng) => _enemyTurn.Attack(tech, rng);

    /// <summary>The active enemy uses one of its battle-table techniques (useEnemyTech,
    /// the enemy's battle-table techniques): pays the MP and deals the damage. Returns false when it cannot afford the
    /// technique — the source shows a "no MP" message but still spends the turn.</summary>
    public bool EnemyTechnique(TechData tech, IBattleRng rng) => _enemyTurn.Technique(tech, rng);

    /// <summary>The enemy switches to another living fighter (FIGHTSTG_pickEnemySwitch). Returns
    /// the new active slot, or -1 when there is no valid target (the turn is still spent).</summary>
    public int EnemySwitch(EnemyDecision decision, IBattleRng rng) => _enemyTurn.Switch(decision, rng);

    /// <summary>The enemy's run-away action (FIGHTSTG_queueRunAway -> WFIGHTMN_runAway ->
    /// FIGHTSTG_testRunAway): ends the battle as fled on success. Returns whether it fled.</summary>
    public bool EnemyRunAway(IBattleRng rng) => _enemyTurn.RunAway(rng);

    /// <summary>The enemy counter-attacks the damage it just received with the given technique
    /// (counterattack reaction). It is a reaction inside the player's
    /// turn, so it does not spend the enemy's turn.</summary>
    public void EnemyCounter(TechData tech, int receivedDamage, IBattleRng rng) => _enemyTurn.Counter(tech, receivedDamage, rng);

    /// <summary>The enemy's turn: decides its action from its battle table
    /// (BattleEnemyAi.PickAction) and executes it. Returns the decision (None when the table is
    /// empty, so the enemy does nothing).</summary>
    public EnemyDecision RunEnemyTurn(IBattleRng rng) => _enemyTurn.RunTurn(rng);

    /// <summary>The enemy's action from its battle table (BattleEnemyAi.PickAction).</summary>
    public EnemyDecision DecideEnemyAction(IBattleRng rng) => _enemyTurn.Decide(rng);

    /// <summary>Creates the hit resolver for one action, with this battle's boost and critical setting.</summary>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>A resolver that consumes <paramref name="rng"/> for its critical rolls.</returns>
    internal BattleHitResolver Hits(IBattleRng rng) => new(Boost, AllowCritical, rng);

    /// <summary>Re-arms the enemy cadence after it spent its turn.</summary>
    internal void AdvanceEnemyTurn() => NextEnemyTurn += TurnDelay(ActiveEnemyFighter, ActivePlayerFighter);

    /// <summary>
    /// Ends the battle once a side has no living fighter: won when every enemy slot is down
    /// (checked first, so a mutual knock-out is a win), lost when every party slot is down.
    /// An outcome already set, such as a flight, is never replaced.
    /// </summary>
    internal void CheckOutcome()
    {
        if (Outcome != BattleOutcome.None)
        {
            return;
        }

        if (!BattleRoster.AnyLiving(Enemies))
        {
            Outcome = BattleOutcome.Won;
        }
        else if (!BattleRoster.AnyLiving(Players))
        {
            Outcome = BattleOutcome.Lost;
        }
    }

    /// <summary>The delay before <paramref name="own"/> acts again against <paramref name="other"/>, or 0 when either is missing.</summary>
    /// <param name="own">The fighter whose next turn is scheduled.</param>
    /// <param name="other">The opposing active fighter.</param>
    /// <returns>The delay in scheduler ticks, 707..1414, or 0.</returns>
    private static int TurnDelay(BattleFighter? own, BattleFighter? other)
    {
        if (own == null || other == null)
        {
            return 0;
        }

        return DamageCalculator.GetTurnDelay(own.Stats.Stats[BattleStat.Speed], other.Stats.Stats[BattleStat.Speed]);
    }

    /// <summary>
    /// Seeds the turn clock: the player acts at time 0 and the enemy at half the player's
    /// first delay. Without both active fighters the enemy clock stays at 0.
    /// </summary>
    private void SeedTurnClock()
    {
        NextPlayerTurn = 0;
        NextEnemyTurn = TurnDelay(ActivePlayerFighter, ActiveEnemyFighter) / 2;
    }

    /// <summary>
    /// The common end of a player hit: the damage lands, the enemy may counter, a knocked-out
    /// enemy is replaced by its next living fighter, and the player's turn is spent.
    /// </summary>
    /// <param name="target">The active enemy that takes the hit.</param>
    /// <param name="damage">The hit's damage.</param>
    /// <param name="rng">The battle's random source, for the counter.</param>
    private void LandPlayerHit(BattleFighter target, int damage, IBattleRng rng)
    {
        target.TakeDamage(damage);
        _enemyTurn.TryCounter(damage, rng);
        ResolveEnemySlot();
        MarkFought();
        AdvancePlayerTurn();
        CheckOutcome();
    }

    private void AdvancePlayerTurn() => NextPlayerTurn += TurnDelay(ActivePlayerFighter, ActiveEnemyFighter);

    /// <summary>WFIGHTMN_knockOut's enemy branch: the enemy side brings in its next living
    /// fighter when the active one falls to a hit. This reinforcement is distinct from the
    /// enemy's own voluntary switch action (FIGHTSTG_pickEnemySwitch, EnemySwitch).</summary>
    private void ResolveEnemySlot()
    {
        if (ActiveEnemyFighter is { IsKnockedOut: true })
        {
            ActiveEnemy = BattleRoster.NextLivingIndex(Enemies, ActiveEnemy);
        }
    }

    private void MarkFought()
    {
        if (ActivePlayer >= 0 && ActivePlayer < Fought.Length)
        {
            Fought[ActivePlayer] = true;
        }
    }
}
