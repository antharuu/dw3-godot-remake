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
/// temporary gauge-triggered digivolution, not a command) remains out of scope. Persistent
/// poison plus the sleep, paralysis and confusion control statuses are coordinated by
/// <see cref="BattleStatusCoordinator"/>.</para>
/// </remarks>
public sealed class BattleState
{
    private readonly EnemyTurnResolver _enemyTurn;
    private readonly BattleTurnScheduler _turnScheduler = new();
    private readonly BattleStatusCoordinator _statusCoordinator;
    private readonly BattleModifierCoordinator _modifierCoordinator;

    /// <summary>Initializes a new instance of the <see cref="BattleState"/> class with empty sides and no outcome.</summary>
    public BattleState()
    {
        _enemyTurn = new EnemyTurnResolver(this);
        _statusCoordinator = new BattleStatusCoordinator(this, _turnScheduler);
        _modifierCoordinator = new BattleModifierCoordinator(this, _turnScheduler);
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
        if (attacker is not { IsKnockedOut: false } || target == null || (attacker.Flags & FighterFlags.Asleep) != 0)
        {
            return false;
        }

        LandPlayerHit(target, tech, Hits(rng).Basic(tech, attacker, target, enemyAttacker: false), rng);
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
        if (attacker is not { IsKnockedOut: false } || target == null || tech.Mp > attacker.Mp || (attacker.Flags & (FighterFlags.Asleep | FighterFlags.Confused)) != 0)
        {
            return false; // an unaffordable technique does not spend the turn
        }

        attacker.RefreshStats();
        target.RefreshStats();
        attacker.Mp -= tech.Mp;
        LandPlayerHit(target, tech, Hits(rng).Technique(tech, attacker, target, enemyAttacker: false), rng);
        _modifierCoordinator.ConsumeCharge(BattleSide.Player);
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
        if (attacker is not { IsKnockedOut: false } || (attacker.Flags & (FighterFlags.NoDigivolve | FighterFlags.Asleep | FighterFlags.Confused)) != 0 || form == attacker.Id)
        {
            return false;
        }

        attacker.Id = form;
        attacker.RefreshStats();
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
        if ((ActivePlayerFighter?.Flags & (FighterFlags.NoSwitch | FighterFlags.Confused)) != 0 || slot < 0 || slot >= Players.Length || slot == ActivePlayer || Players[slot] is not { Id: not 0, IsKnockedOut: false })
        {
            return false;
        }

        ActivePlayer = slot;
        AdvancePlayerTurn();
        return true;
    }

    /// <summary>Applies one temporary attack, defense, or speed modifier to a living fighter.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Target fighter slot.</param>
    /// <param name="stat">Attack, defense, or speed stat to modify.</param>
    /// <param name="percent">Delta in 128ths of the unmodified stat.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether the modifier was applied.</returns>
    public bool ApplyStatModifier(BattleSide side, int fighterSlot, int stat, int percent, IBattleRng rng) =>
        _modifierCoordinator.ApplyStatModifier(side, fighterSlot, stat, percent, fromTechnique: false, rng);

    /// <summary>Applies a temporary elemental field boost that restores the previous field when its scheduled expiry is due.</summary>
    /// <param name="boost">Element and amount active for the temporary field window.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether a supported elemental field was applied.</returns>
    public bool ApplyElementBoost(ElementBoost boost, IBattleRng rng) => _modifierCoordinator.ApplyElementBoost(boost, rng);

    /// <summary>Applies a temporary special state to one living fighter.</summary>
    /// <param name="side">Side containing the fighter.</param>
    /// <param name="fighterSlot">Slot of the fighter receiving the state.</param>
    /// <param name="rng">Shared battle random source for expiry timing.</param>
    /// <returns>Whether a living fighter received the state.</returns>
    public bool ApplySpecialStatus(BattleSide side, int fighterSlot, IBattleRng rng) =>
        _modifierCoordinator.ApplySpecialStatus(side, fighterSlot, rng);

    /// <summary>Sets the active fighter's charge for its next technique.</summary>
    /// <param name="side">Side containing the active fighter.</param>
    /// <param name="value">Charge in sixty-fourths of damage.</param>
    /// <returns>Whether a living fighter received the charge.</returns>
    public bool SetCharge(BattleSide side, int value) => _modifierCoordinator.SetCharge(side, value);

    /// <summary>Attempts to poison the opposing active fighter using the qualified poison resistance roll.</summary>
    /// <param name="attackerSide">Side inflicting poison.</param>
    /// <param name="chance">Base application chance.</param>
    /// <param name="power">Stored periodic poison strength.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether poison was applied or refreshed.</returns>
    public bool InflictPoison(BattleSide attackerSide, int chance, int power, IBattleRng rng)
        => _statusCoordinator.InflictPoison(attackerSide, chance, power, rng);

    /// <summary>Cures poison from one fighter and removes its scheduled periodic damage.</summary>
    /// <param name="side">Side containing the cured fighter.</param>
    /// <param name="fighterSlot">Slot of the cured fighter.</param>
    public void CurePoison(BattleSide side, int fighterSlot)
        => _statusCoordinator.CurePoison(side, fighterSlot);

    /// <summary>Attempts to paralyze the opposing active fighter with a source-strength counter.</summary>
    /// <param name="attackerSide">Side applying paralysis.</param>
    /// <param name="chance">Base application chance.</param>
    /// <param name="strength">Paralysis strength stored on success.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether paralysis was applied or refreshed.</returns>
    public bool InflictParalysis(BattleSide attackerSide, int chance, int strength, IBattleRng rng) =>
        _statusCoordinator.InflictControl(attackerSide, chance, strength, new(FighterFlags.Paralyzed, BattleScheduledEventKind.ParalysisEnd, Resist.Paralysis, 4), rng);

    /// <summary>Attempts to confuse the opposing active fighter with a source-strength counter.</summary>
    /// <param name="attackerSide">Side applying confusion.</param>
    /// <param name="chance">Base application chance.</param>
    /// <param name="strength">Confusion strength stored on success.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether confusion was applied or refreshed.</returns>
    public bool InflictConfusion(BattleSide attackerSide, int chance, int strength, IBattleRng rng) =>
        _statusCoordinator.InflictControl(attackerSide, chance, strength, new(FighterFlags.Confused, BattleScheduledEventKind.ConfusionEnd, Resist.Confusion, 3), rng);

    /// <summary>Attempts to put the opposing active fighter to sleep with a source-strength counter.</summary>
    /// <param name="attackerSide">Side applying sleep.</param>
    /// <param name="chance">Base application chance.</param>
    /// <param name="strength">Sleep strength stored on success.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether sleep was applied or refreshed.</returns>
    public bool InflictSleep(BattleSide attackerSide, int chance, int strength, IBattleRng rng) =>
        _statusCoordinator.InflictControl(attackerSide, chance, strength, new(FighterFlags.Asleep, BattleScheduledEventKind.SleepEnd, Resist.Sleep, 2), rng);

    /// <summary>Clears all three control statuses and their scheduled expiries from one fighter.</summary>
    /// <param name="side">Side containing the cured fighter.</param>
    /// <param name="fighterSlot">Slot of the cured fighter.</param>
    public void CureControlStatuses(BattleSide side, int fighterSlot)
        => _statusCoordinator.CureControlStatuses(side, fighterSlot);

    /// <summary>Tests whether the active player loses its turn to paralysis and spends that turn when it does.</summary>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether paralysis consumed the turn.</returns>
    public bool TryLosePlayerTurnToParalysis(IBattleRng rng)
    {
        if (!_statusCoordinator.LosesPlayerTurnToParalysis(rng))
        {
            return false;
        }

        AdvancePlayerTurn();
        return true;
    }

    /// <summary>Tests whether the active enemy loses its turn to paralysis and spends that turn when it does.</summary>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether paralysis consumed the turn.</returns>
    public bool TryLoseEnemyTurnToParalysis(IBattleRng rng)
    {
        if (!_statusCoordinator.LosesEnemyTurnToParalysis(rng))
        {
            return false;
        }

        AdvanceEnemyTurn();
        return true;
    }

    /// <summary>Tests whether a hit wakes the active fighter of one side and removes its pending sleep expiry.</summary>
    /// <param name="side">Side whose active fighter received the hit.</param>
    /// <param name="damage">Resolved damage value.</param>
    /// <param name="rng">Shared battle random source.</param>
    /// <returns>Whether sleep ended.</returns>
    public bool TryWakeOnHit(BattleSide side, int damage, IBattleRng rng)
        => _statusCoordinator.TryWakeOnHit(side, damage, rng);

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
        if (attacker is not { IsKnockedOut: false } || (attacker.Flags & FighterFlags.Confused) != 0)
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
        if (attacker is not { IsKnockedOut: false } || defender == null || (attacker.Flags & (FighterFlags.Asleep | FighterFlags.Confused | FighterFlags.NoSwitch)) != 0)
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

    /// <summary>Consumes the active fighter's pending charge after a technique.</summary>
    /// <param name="side">Side whose active fighter's charge is cleared.</param>
    internal void ConsumeCharge(BattleSide side) => _modifierCoordinator.ConsumeCharge(side);

    /// <summary>Applies one supported technique effect after its damage has landed.</summary>
    /// <param name="attackerSide">Side that landed the technique.</param>
    /// <param name="technique">Technique supplying the extracted effect fields.</param>
    /// <param name="rng">Shared battle random source.</param>
    internal void ApplyTechniqueEffect(BattleSide attackerSide, TechData technique, IBattleRng rng) =>
        _ = BattleTechniqueEffectResolver.ApplyLanded(this, attackerSide, technique, rng);

    /// <summary>Gets the active fighter on one side.</summary>
    /// <param name="side">Requested side.</param>
    /// <returns>The active fighter, if one exists.</returns>
    internal BattleFighter? FighterFor(BattleSide side) =>
        side == BattleSide.Player ? ActivePlayerFighter : ActiveEnemyFighter;

    /// <summary>Gets a fighter by side and slot without throwing on invalid input.</summary>
    /// <param name="side">Requested side.</param>
    /// <param name="slot">Requested slot.</param>
    /// <returns>Fighter at the slot, if valid.</returns>
    internal BattleFighter? FighterAt(BattleSide side, int slot)
    {
        BattleFighter?[] fighters = side == BattleSide.Player ? Players : Enemies;
        return slot >= 0 && slot < fighters.Length ? fighters[slot] : null;
    }

    /// <summary>Gets the active slot of one side.</summary>
    /// <param name="side">Requested side.</param>
    /// <returns>The active slot, or -1 when no fighter is active.</returns>
    internal int ActiveSlot(BattleSide side) => side == BattleSide.Player ? ActivePlayer : ActiveEnemy;

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
    /// <param name="technique">Technique which supplied the landed result.</param>
    /// <param name="result">The resolved hit, which may miss after consuming its qualified random draws.</param>
    /// <param name="rng">The battle's random source, for the counter.</param>
    private void LandPlayerHit(BattleFighter target, TechData technique, BattleHitResult result, IBattleRng rng)
    {
        if (result.Landed)
        {
            target.TakeDamage(result.Damage);
            _ = TryWakeOnHit(BattleSide.Enemy, result.Damage, rng);
            ApplyTechniqueEffect(BattleSide.Player, technique, rng);
            _enemyTurn.TryCounter(result.Damage, rng);
            ResolveEnemySlot();
        }

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
        ProcessDueEvents();
        _ = _turnScheduler.PopNext();
        var own = side == BattleSide.Player ? ActivePlayerFighter : ActiveEnemyFighter;
        var other = side == BattleSide.Player ? ActiveEnemyFighter : ActivePlayerFighter;
        _turnScheduler.EnqueueActionTurn(side, kind, BattleFighterTiming.SpeedOf(own), BattleFighterTiming.SpeedOf(other), BattleFighterTiming.SpiritOf(own), rng ?? new BattleDefaultOpeningRng());
        ProcessDueEvents();
    }

    /// <summary>Consumes every due modifier and status event before the next ordinary action.</summary>
    private void ProcessDueEvents()
    {
        bool any;
        do
        {
            any = false;
            while (_modifierCoordinator.ProcessNextExpiry())
            {
                any = true;
            }

            if (_statusCoordinator.ProcessDueEvents())
            {
                any = true;
            }
        }
        while (any);
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
