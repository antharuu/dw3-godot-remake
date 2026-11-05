namespace Dw3Production.Simulation;

/// <summary>
/// The 1v1 battle state and its turn loop: three fighters per side stored but one active,
/// the element boost, run attempts, and a deterministic time-to-turn scheduler built on
/// <see cref="DamageCalculator.GetTurnDelay"/>. Engine-free, like <see cref="WarpController"/>.
/// </summary>
/// <remarks>
/// <para>Fidelity vs simplification: the original game runs a real-time event queue with
/// per-kind delays and a menu state machine; this core collapses that to a two-actor
/// conditional-turn scheduler whose per-side delay uses the formula of a plain action,
/// seeded as the original seeds it (player at 0, enemy at half the player's cycle).</para>
/// <para>Each of the six battle menu commands is exposed here: attack (first learned skill),
/// technique (a picked skill, MP spent), digivolve (a permanent change to another of the
/// partner's forms, no MP cost, gated only by <see cref="FighterFlags.NoDigivolve"/>), switch (a
/// manual change of the active partner), item (a battle-usable item) and flee. Knock-outs do NOT
/// auto-switch the player to the next fighter: the battle ends in defeat when no partner is
/// alive, and the switch is the manual Switch command (the Switch and SwitchIn menus). The enemy
/// side decides its turn from its fighter table (<see cref="BattleEnemyAi"/>, through
/// <see cref="EnemyTurnResolver"/>): attack, technique, switch or run away, plus the
/// counter-attack its counter field triggers when the player's hit lands. The blast (a
/// temporary gauge-triggered digivolution, not a command) and statuses are out of scope.</para>
/// </remarks>
public sealed class BattleState
{
    private readonly EnemyTurnResolver _enemyTurn;
    private readonly BattleTurnScheduler _turnScheduler = new();

    /// <summary>Initializes a new instance of the <see cref="BattleState"/> class with empty sides and no outcome.</summary>
    public BattleState()
    {
        _enemyTurn = new EnemyTurnResolver(this);
    }

    /// <summary>Gets or sets the three party slots. A null slot is empty.</summary>
    public BattleFighter?[] Players { get; set; } = new BattleFighter?[3];

    /// <summary>Gets or sets the three enemy slots. A null slot is empty.</summary>
    public BattleFighter?[] Enemies { get; set; } = new BattleFighter?[3];

    /// <summary>Gets or sets the slot of the partner currently fighting, 0..2, or -1 before set up or when no partner is alive.</summary>
    public int ActivePlayer { get; set; } = -1;

    /// <summary>Gets or sets the slot of the enemy currently fighting, 0..2, or -1 when no enemy is alive.</summary>
    public int ActiveEnemy { get; set; } = -1;

    /// <summary>Gets or sets the number of run away attempts made by the player in this battle. Each failure raises the next chance by 8 out of 128.</summary>
    public int RunAttempts { get; set; }

    /// <summary>Gets or sets a battle kind value kept for callers. The current rules do not read it.</summary>
    public int Kind { get; set; }

    /// <summary>Gets or sets the battlefield's element boost (none by default).</summary>
    public ElementBoost Boost { get; set; }

    /// <summary>Gets or sets the encounter index of the battle, tested by enemy AI condition 12.</summary>
    public int BattleId { get; set; }

    /// <summary>Gets or sets the encounter kind of the battle, tested by enemy AI condition 13.</summary>
    public int EncounterKind { get; set; }

    /// <summary>Gets or sets the Digimon ids of the eight partners (the first eight entries of the Digimon table), tested by enemy AI condition 8.</summary>
    public IReadOnlyCollection<int> StarterDigimonIds { get; set; } = [];

    /// <summary>Gets or sets the technique catalogue (id -> resolved technique), for resolving the enemy's
    /// fighter table techniques on its turn and its counter-attack.</summary>
    public IReadOnlyDictionary<int, TechData> Techs { get; set; } = new Dictionary<int, TechData>();

    /// <summary>Gets or sets the twelve block flags of the encounter. Only <see cref="BattleConst.BlockRunAway"/> is applied today.</summary>
    public int[] Blocks { get; set; } = new int[12];

    /// <summary>Gets or sets how the battle ended. Once set to a value other than <see cref="BattleOutcome.None"/> it is never replaced by the outcome check.</summary>
    public BattleOutcome Outcome { get; set; }

    /// <summary>Gets or sets, per party slot, whether that partner took a turn this
    /// battle. The battle report shares the earned exp only among the partners who took a turn.</summary>
    public bool[] Fought { get; set; } = new bool[3];

    /// <summary>Gets or sets a value indicating whether criticals roll; when false (default) damage is deterministic.</summary>
    public bool AllowCritical { get; set; }

    /// <summary>Gets the player side's remaining event delay.</summary>
    public int NextPlayerTurn => _turnScheduler.RemainingTurnDelay(BattleSide.Player);

    /// <summary>Gets the enemy side's remaining event delay.</summary>
    public int NextEnemyTurn => _turnScheduler.RemainingTurnDelay(BattleSide.Enemy);

    /// <summary>Gets the partner in the active slot, null when no slot is active.</summary>
    public BattleFighter? ActivePlayerFighter => ActivePlayer >= 0 ? Players[ActivePlayer] : null;

    /// <summary>Gets the enemy in the active slot, null when no slot is active.</summary>
    public BattleFighter? ActiveEnemyFighter => ActiveEnemy >= 0 ? Enemies[ActiveEnemy] : null;

    /// <summary>Gets the side whose next queued turn wins the EU slot-sensitive selection.</summary>
    public BattleSide NextActor => _turnScheduler.Count == 0 ? BattleSide.Player : _turnScheduler.PeekNextTurnSide();

    /// <summary>
    /// Fills the three partners (the first with the active Digimon)
    /// and the three enemies, loading HP/MP and resolving stats, then seeds the turn clock.
    /// </summary>
    /// <param name="setup">The battle set up by the encounter.</param>
    /// <param name="digimon">Digimon id slot 0 fights as, which may differ from its seed after a digivolution on the field.</param>
    /// <param name="openingRng">Injected shared opening stream. Only legacy and synthetic callers may omit it for a fixed compatibility roll.</param>
    /// <returns>A state on the first living fighter of each side. Its outcome is already set when a side starts with no living fighter.</returns>
    public static BattleState InitFighters(BattleSetup setup, int digimon, IBattleRng? openingRng = null)
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

            // The first partner fights as the chosen digimon; the other two as themselves.
            int fighterDigimon = i == 0 ? digimon : seed?.Id ?? 0;
            state.Players[i] = seed == null ? null : BattleFighter.FromPartner(seed, fighterDigimon);
            var enemy = setup.Enemies[i];
            state.Enemies[i] = enemy is null or { Fighter: 0 } ? null : BattleFighter.FromEnemy(enemy);
        }

        state.ActivePlayer = BattleRoster.FirstLivingIndex(state.Players);
        state.ActiveEnemy = BattleRoster.FirstLivingIndex(state.Enemies);
        state.SeedTurnClock(setup.AmbushChance, openingRng ?? new BattleDefaultOpeningRng());
        state.CheckOutcome();
        return state;
    }

    /// <summary>The active partner attacks the active enemy with its basic technique.</summary>
    /// <param name="tech">The basic attack technique.</param>
    /// <param name="rng">The battle's random source, for the critical roll and the counter.</param>
    /// <returns><see langword="false"/> when the active partner is knocked out or no enemy is active, otherwise <see langword="true"/> with the turn spent.</returns>
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
    /// <param name="tech">The technique picked in the menu.</param>
    /// <param name="rng">The battle's random source, for the critical roll and the counter.</param>
    /// <returns><see langword="false"/> without spending the turn when the technique costs more MP than the partner has or nobody can fight, otherwise <see langword="true"/>.</returns>
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
    /// <param name="form">Digimon id of the target form.</param>
    /// <returns><see langword="true"/> when the form changed and the turn was spent.</returns>
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
    /// <param name="slot">Party slot to bring in, 0..2.</param>
    /// <returns><see langword="false"/> for an out-of-range, active, empty or knocked-out slot, otherwise <see langword="true"/>.</returns>
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
    /// The active partner uses a battle item. The effect is applied to
    /// the active fighter (or the party for revive); removing the item from the bag is the
    /// flow controller's concern.
    /// </summary>
    /// <param name="item">The item used.</param>
    /// <returns><see langword="false"/> when the active partner is knocked out, otherwise <see langword="true"/> with the turn spent.</returns>
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

    /// <summary>The active partner tries to run away. A success ends the battle as fled, a failure spends the turn.</summary>
    /// <param name="rng">The battle's random source, one value drawn per attempt.</param>
    /// <returns><see langword="true"/> when the party fled. Also <see langword="false"/> without an attempt when nobody can fight.</returns>
    public bool PlayerFlee(IBattleRng rng)
    {
        var attacker = ActivePlayerFighter;
        var defender = ActiveEnemyFighter;
        if (attacker is not { IsKnockedOut: false } || defender == null)
        {
            return false;
        }

        RunAttempts++;
        bool fled = RunAwayRules.TestRunAway(
            RunAttempts,
            attacker.Stats,
            defender.Stats,
            Blocks[BattleConst.BlockRunAway] != 0,
            attacker.Flags,
            rng.NextValue());
        if (fled)
        {
            Outcome = BattleOutcome.Fled;
            return true;
        }

        AdvancePlayerTurn(BattleActionDelayKind.RunAway, rng);
        return false;
    }

    /// <summary>The active enemy attacks the active partner with the given technique (its basic
    /// attack on a turn).</summary>
    /// <param name="tech">The enemy's basic attack.</param>
    /// <param name="rng">The battle's random source.</param>
    public void EnemyAttack(TechData tech, IBattleRng rng) => _enemyTurn.Attack(tech, rng);

    /// <summary>The active enemy uses one of its fighter table techniques: pays the MP and deals the damage.</summary>
    /// <param name="tech">The technique picked by the enemy AI.</param>
    /// <param name="rng">The battle's random source.</param>
    /// <returns><see langword="false"/> when the enemy cannot afford the technique. The original game shows a "no MP" message and still spends the turn.</returns>
    public bool EnemyTechnique(TechData tech, IBattleRng rng) => _enemyTurn.Technique(tech, rng);

    /// <summary>The enemy switches to another living fighter.</summary>
    /// <param name="decision">The switch decision of the enemy AI.</param>
    /// <param name="rng">The battle's random source, used when the reserve is picked at random.</param>
    /// <returns>The new active slot, or -1 when there is no valid target (the turn is still spent).</returns>
    public int EnemySwitch(EnemyDecision decision, IBattleRng rng) => _enemyTurn.Switch(decision, rng);

    /// <summary>The enemy's run-away action: ends the battle as fled on success.</summary>
    /// <param name="rng">The battle's random source, one value drawn.</param>
    /// <returns>Whether the enemy fled.</returns>
    public bool EnemyRunAway(IBattleRng rng) => _enemyTurn.RunAway(rng);

    /// <summary>The enemy counter-attacks the damage it just received with the given technique.
    /// It is a reaction inside the player's turn, so it does not spend the enemy's turn.</summary>
    /// <param name="tech">The counter technique, whose effect power scales the received damage.</param>
    /// <param name="receivedDamage">Damage the enemy took from the hit it answers.</param>
    /// <param name="rng">The battle's random source.</param>
    public void EnemyCounter(TechData tech, int receivedDamage, IBattleRng rng) => _enemyTurn.Counter(tech, receivedDamage, rng);

    /// <summary>The enemy's turn: decides its action from its fighter table
    /// (<see cref="BattleEnemyAi.PickAction"/>) and executes it.</summary>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>The decision taken, <see cref="EnemyDecision.None"/> when the table is empty and the enemy does nothing.</returns>
    public EnemyDecision RunEnemyTurn(IBattleRng rng) => _enemyTurn.RunTurn(rng);

    /// <summary>Decides the enemy's action from its fighter table without executing it.</summary>
    /// <param name="rng">The battle's random source, drawn by chance conditions.</param>
    /// <returns>The decision, <see cref="EnemyDecision.None"/> when no enemy is active or its table is empty.</returns>
    public EnemyDecision DecideEnemyAction(IBattleRng rng) => _enemyTurn.Decide(rng);

    /// <summary>Creates the hit resolver for one action, with this battle's boost and critical setting.</summary>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>A resolver that consumes <paramref name="rng"/> for its critical rolls.</returns>
    internal BattleHitResolver Hits(IBattleRng rng) => new(Boost, AllowCritical, rng);

    /// <summary>Re-arms the enemy cadence after it spent its turn.</summary>
    internal void AdvanceEnemyTurn() => ScheduleTurn(BattleSide.Enemy, BattleActionDelayKind.NormalTurn, null);

    /// <summary>Re-arms the enemy after an unsuccessful run-away attempt.</summary>
    /// <param name="rng">The battle random source used for the attempt.</param>
    internal void AdvanceEnemyRunAway(IBattleRng rng) => ScheduleTurn(BattleSide.Enemy, BattleActionDelayKind.RunAway, rng);

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

    /// <summary>Seeds the EU relative queue with the encounter opening rule.</summary>
    /// <param name="ambushChance">Encounter ambush probability, or zero when disabled.</param>
    /// <param name="rng">Shared injected battle random source.</param>
    private void SeedTurnClock(int ambushChance, IBattleRng rng) =>
        _turnScheduler.InitializeOpening(BattleFighterTiming.SpeedOf(ActivePlayerFighter), BattleFighterTiming.SpeedOf(ActiveEnemyFighter), ambushChance, rng);

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

    /// <summary>Re-arms the player cadence after an ordinary completed action.</summary>
    private void AdvancePlayerTurn() => ScheduleTurn(BattleSide.Player, BattleActionDelayKind.NormalTurn, null);

    /// <summary>Re-arms the player cadence after an action family that consumes the shared random stream.</summary>
    /// <param name="kind">Qualified delay family for the completed player action.</param>
    /// <param name="rng">The battle random source used by that action.</param>
    private void AdvancePlayerTurn(BattleActionDelayKind kind, IBattleRng rng) => ScheduleTurn(BattleSide.Player, kind, rng);

    /// <summary>Consumes the due side event once and schedules its replacement from current fighter values.</summary>
    /// <param name="side">Side that completed an action.</param>
    /// <param name="kind">Qualified replacement delay family.</param>
    /// <param name="rng">Random source when the family needs a draw, otherwise null.</param>
    private void ScheduleTurn(BattleSide side, BattleActionDelayKind kind, IBattleRng? rng)
    {
        _ = _turnScheduler.PopNext();
        var own = side == BattleSide.Player ? ActivePlayerFighter : ActiveEnemyFighter;
        var other = side == BattleSide.Player ? ActiveEnemyFighter : ActivePlayerFighter;
        _turnScheduler.EnqueueActionTurn(side, kind, BattleFighterTiming.SpeedOf(own), BattleFighterTiming.SpeedOf(other), BattleFighterTiming.SpiritOf(own), rng ?? new BattleDefaultOpeningRng());
    }

    /// <summary>The enemy side brings in its next living
    /// fighter when the active one falls to a hit. This reinforcement is distinct from the
    /// enemy's own voluntary switch action (<see cref="EnemySwitch"/>).</summary>
    private void ResolveEnemySlot()
    {
        if (ActiveEnemyFighter is { IsKnockedOut: true })
        {
            ActiveEnemy = BattleRoster.NextLivingIndex(Enemies, ActiveEnemy);
        }
    }

    /// <summary>Records that the active partner took a turn, so it gets a share of the experience.</summary>
    private void MarkFought()
    {
        if (ActivePlayer >= 0 && ActivePlayer < Fought.Length)
        {
            Fought[ActivePlayer] = true;
        }
    }
}
