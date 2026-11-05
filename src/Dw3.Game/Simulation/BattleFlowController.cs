using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// The battle's turn flow, the thin engine-free wrapper over the <see cref="BattleState"/> that
/// the presentation drives. It owns the menu phase (player command vs enemy turn), the six
/// commands of the battle menu (attack, technique, digivolve, switch, item, run), the digivolution and item
/// catalogues, and a one-line message. Every rule delegates to <see cref="BattleState"/>: damage,
/// fleeing, digivolving, switching, items and the conditional-turn scheduler all stay in the
/// simulation core, and victory or defeat come from its outcome check.
/// </summary>
public sealed class BattleFlowController
{
    /// <summary>Gets or sets the technique used by the Attack command: the active partner's first learned skill,
    /// which is how the original menu resolves its basic attack.</summary>
    public TechData PlayerBasicTech { get; set; } = new();

    /// <summary>Gets or sets the enemy's basic attack, the first technique of its fighter table entry.</summary>
    public TechData EnemyBasicTech { get; set; } = new();

    /// <summary>Gets or sets the active partner's techniques listed in the Technique submenu. The host refreshes it when the active partner changes.</summary>
    public IReadOnlyList<BattleTechnique> PlayerTechniques { get; set; } = [];

    /// <summary>Gets or sets the technique catalogue (id to resolved technique), used to resolve the enemy's
    /// fighter table techniques on its turn. It is copied into the battle state at <see cref="Start"/>.</summary>
    public IReadOnlyDictionary<int, TechData> Techs { get; set; } = new Dictionary<int, TechData>();

    /// <summary>Gets or sets the Digimon forms the active partner can digivolve into,
    /// for the Digivolve submenu. The host refreshes it when the active partner changes.</summary>
    public IReadOnlyList<int> PlayerDigivolutions { get; set; } = [];

    /// <summary>Gets or sets the battle item catalogue keyed by item id. An item missing from it cannot be used.</summary>
    public IReadOnlyDictionary<int, BattleItem> Items { get; set; } = new Dictionary<int, BattleItem>();

    /// <summary>Gets or sets the bag: item id to how many the party holds. Using an item
    /// decrements it, and the item menu lists only the ids with a positive count.</summary>
    public Dictionary<int, int> ItemStock { get; set; } = [];

    /// <summary>Gets or sets the item names as text token streams, keyed by item id, decoded with the battle item catalogue. They fill the item message.</summary>
    public IReadOnlyDictionary<int, BattleTextLine> ItemText { get; set; } = new Dictionary<int, BattleTextLine>();

    /// <summary>Gets or sets the map from fighter id to the name id of the Digimon name table. Enemies take it from their fighter table entry, partners from their Digimon entry.</summary>
    public IReadOnlyDictionary<int, int> NameIds { get; set; } = new Dictionary<int, int>();

    /// <summary>Gets or sets the battle text catalogue. When null, menu lines and messages are <see cref="BattleTextLine.Missing"/> and the battle still runs.</summary>
    public BattleTextCatalog? Text { get; set; }

    /// <summary>Gets the text of the last message, with the actor, technique or item names substituted. <see cref="BattleTextLine.Missing"/> when no message is pending.</summary>
    public BattleTextLine MessageText { get; private set; } = BattleTextLine.Missing;

    /// <summary>Gets the battle state the flow drives, replaced at each <see cref="Start"/>.</summary>
    public BattleState State { get; private set; } = new();

    /// <summary>Gets whose turn it is, or <see cref="BattleFlowPhase.Ended"/> once an outcome is known.</summary>
    public BattleFlowPhase Phase { get; private set; } = BattleFlowPhase.PlayerCommand;

    /// <summary>Gets the debug label of the last message, written to the action log. Empty when no message is shown.</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the battle message is shown below the action area. Menus never retain it.</summary>
    public bool MessageVisible { get; private set; }

    /// <summary>Gets the outcome of the battle state, <see cref="BattleOutcome.None"/> while the battle goes on.</summary>
    public BattleOutcome Outcome => State.Outcome;

    /// <summary>Begins a battle from an already-initialised state, on the player's turn.</summary>
    /// <param name="state">The initialised state, whose technique catalogue is replaced by <see cref="Techs"/>.</param>
    public void Start(BattleState state)
    {
        State = state;
        State.Techs = Techs;
        Phase = State.NextActor == BattleSide.Player ? BattleFlowPhase.PlayerCommand : BattleFlowPhase.EnemyTurn;
        ClearMessage();
    }

    /// <summary>Resolves the player's command and advances the phase. <paramref name="arg"/> is
    /// the technique id (Technique), the Digimon form id (Digivolve), the partner slot (Switch)
    /// or the item id (Item), ignored for Attack and Flee. Returns false when the command was
    /// not spendable (an unaffordable technique, a barred digivolution, an invalid switch or a
    /// missing item), leaving the menu open.</summary>
    /// <param name="command">The command picked in the root menu or a submenu.</param>
    /// <param name="arg">Technique id, form id, party slot (0..2) or item id, depending on <paramref name="command"/>.</param>
    /// <param name="rng">The battle's random source, drawn by hits and run away.</param>
    /// <returns><see langword="true"/> when the turn was spent and the phase advanced, <see langword="false"/> outside the player's turn or for a refused command.</returns>
    public bool Choose(BattleCommand command, int arg, IBattleRng rng)
    {
        if (Phase != BattleFlowPhase.PlayerCommand)
        {
            return false;
        }

        var actor = State.ActivePlayerFighter;
        bool spent = command switch
        {
            BattleCommand.Attack => ChooseAttack(actor, rng),
            BattleCommand.Technique => ChooseTechnique(actor, arg, rng),
            BattleCommand.Digivolve => ChooseDigivolve(actor, arg),
            BattleCommand.Switch => ChooseSwitch(arg),
            BattleCommand.Item => ChooseItem(actor, arg),
            BattleCommand.Flee => ChooseFlee(actor, rng),
            _ => true,
        };
        if (!spent)
        {
            return false;
        }

        AdvancePhase();
        return true;
    }

    /// <summary>Resolves the enemy's turn: it decides its action from its battle table
    /// (attack/technique/switch/run-away; its counter-attack fires inside the player's hit) and
    /// executes it, then advances the phase. Does nothing outside the enemy's turn.</summary>
    /// <param name="rng">The battle's random source, drawn by the action conditions and the hit.</param>
    public void EnemyAct(IBattleRng rng)
    {
        if (Phase != BattleFlowPhase.EnemyTurn)
        {
            return;
        }

        var decision = State.RunEnemyTurn(rng);
        var enemy = State.ActiveEnemyFighter;
        SetMessage(decision.Kind is EnemyActionKind.Technique1 or EnemyActionKind.Technique2 ? "technique" : "actor", NameTokens(enemy));
        AdvancePhase();
    }

    /// <summary>
    /// The six-command root menu lines, greyed by the active fighter's state:
    /// attack/technique/digivolve are greyed while
    /// asleep, digivolve while <see cref="FighterFlags.NoDigivolve"/> is set, and everything but Switch once the
    /// active partner is knocked out (the manual Switch is the only command left then).
    /// </summary>
    /// <returns>Six lines in <see cref="BattleCommand"/> order, each with its enabled state.</returns>
    public IReadOnlyList<BattleMenuLine> RootMenuLines()
    {
        var fighter = State.ActivePlayerFighter;
        bool asleep = fighter is { } f && (f.Flags & FighterFlags.Asleep) != 0;
        bool noDigivolve = fighter is { } g && (g.Flags & FighterFlags.NoDigivolve) != 0;
        bool knockedOut = fighter is not { IsKnockedOut: false };
        return
        [
            Line(0, !knockedOut && !asleep), Line(1, !knockedOut && !asleep),
            Line(2, !knockedOut && !asleep && !noDigivolve), Line(3, !knockedOut),
            Line(4, !knockedOut), Line(5, !knockedOut),
        ];
    }

    /// <summary>Force-ends the battle with the given outcome: the test driver sets the battle's result directly,
    /// so a defeat can be exercised without a contrived fight. Production reaches Ended through
    /// <see cref="AdvancePhase"/> after the outcome check fires.</summary>
    /// <param name="outcome">Outcome written to the battle state.</param>
    public void EndBattle(BattleOutcome outcome)
    {
        State.Outcome = outcome;
        Phase = BattleFlowPhase.Ended;
    }

    /// <summary>Consumes one of <paramref name="itemId"/> from the bag and applies its effect
    /// only when the item is in the catalogue, the stock is positive and the battle state accepts the effect.</summary>
    /// <param name="itemId">Item id picked in the Item submenu.</param>
    /// <returns><see langword="true"/> when the item was used and the stock decremented.</returns>
    private bool UseItem(int itemId)
    {
        if (!Items.TryGetValue(itemId, out var item) || !ItemStock.TryGetValue(itemId, out var count) || count <= 0)
        {
            return false;
        }

        if (!State.PlayerItem(item))
        {
            return false;
        }

        ItemStock[itemId] = count - 1;
        return true;
    }

    /// <summary>Moves to the next phase after a spent turn: <see cref="BattleFlowPhase.Ended"/> with a won or lost message once an outcome exists, otherwise the turn of whichever side the state schedules next. The message is cleared when the player gets the menu back.</summary>
    private void AdvancePhase()
    {
        if (State.Outcome != BattleOutcome.None)
        {
            if (State.Outcome == BattleOutcome.Won)
            {
                SetMessage("won");
            }
            else if (State.Outcome == BattleOutcome.Lost)
            {
                SetMessage("lost");
            }

            Phase = BattleFlowPhase.Ended;
            return;
        }

        Phase = State.NextActor == BattleSide.Player ? BattleFlowPhase.PlayerCommand : BattleFlowPhase.EnemyTurn;
        if (Phase == BattleFlowPhase.PlayerCommand)
        {
            ClearMessage();
        }
    }

    /// <summary>The basic attack; refused without a message when no hit can land.</summary>
    /// <param name="actor">The active partner before the command, named in the message.</param>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>Whether the command spent the turn.</returns>
    private bool ChooseAttack(BattleFighter? actor, IBattleRng rng)
    {
        if (!State.PlayerAttack(PlayerBasicTech, rng))
        {
            return false;
        }

        SetMessage("basicAttack", NameTokens(actor));
        return true;
    }

    /// <summary>
    /// A technique from the submenu, by technique id. An unknown id or an unaffordable technique
    /// shows the "no MP" message and leaves the menu open.
    /// </summary>
    /// <param name="actor">The active partner before the command, named in the message.</param>
    /// <param name="techniqueId">The picked technique id.</param>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>Whether the command spent the turn.</returns>
    private bool ChooseTechnique(BattleFighter? actor, int techniqueId, IBattleRng rng)
    {
        var technique = PlayerTechniques.FirstOrDefault(candidate => candidate.Id == techniqueId);
        if (technique is null || !State.PlayerTechnique(technique.Tech, rng))
        {
            SetMessage("noMp");
            return false;
        }

        SetMessage("technique", NameTokens(actor), technique.Text?.Tokens ?? []);
        return true;
    }

    /// <summary>A digivolution into the Digimon form id; the message names the form before and after.</summary>
    /// <param name="actor">The active partner before the command, named in the message.</param>
    /// <param name="form">The Digimon form id to change into.</param>
    /// <returns>Whether the command spent the turn.</returns>
    private bool ChooseDigivolve(BattleFighter? actor, int form)
    {
        if (!State.PlayerDigivolve(form))
        {
            return RejectCommand();
        }

        SetMessage("digivolve", NameTokens(actor), NameTokens(State.ActivePlayerFighter));
        return true;
    }

    /// <summary>A manual switch to the party slot; the message names the incoming partner.</summary>
    /// <param name="slot">The party slot (0..2) to bring in.</param>
    /// <returns>Whether the command spent the turn.</returns>
    private bool ChooseSwitch(int slot)
    {
        if (!State.PlayerSwitch(slot))
        {
            return RejectCommand();
        }

        SetMessage("switch", NameTokens(State.ActivePlayerFighter));
        return true;
    }

    /// <summary>An item from the bag, by item id.</summary>
    /// <param name="actor">The active partner, named in the message.</param>
    /// <param name="itemId">The item id picked from the bag.</param>
    /// <returns>Whether the command spent the turn.</returns>
    private bool ChooseItem(BattleFighter? actor, int itemId)
    {
        if (!UseItem(itemId))
        {
            return RejectCommand();
        }

        SetMessage("item", NameTokens(actor), ItemTokens(itemId));
        return true;
    }

    /// <summary>A run-away attempt. It always spends the turn; only a success shows a message.</summary>
    /// <param name="actor">The active partner, named in the message.</param>
    /// <param name="rng">The battle's random source.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    private bool ChooseFlee(BattleFighter? actor, IBattleRng rng)
    {
        if (State.PlayerFlee(rng))
        {
            SetMessage("fledPlayer", NameTokens(actor));
        }

        return true;
    }

    /// <summary>
    /// Refuses a digivolve, switch or item command: the pending message text is dropped while the
    /// visible message line is left as it was.
    /// </summary>
    /// <returns>Always <see langword="false"/>, so a handler can return it directly.</returns>
    private bool RejectCommand()
    {
        MessageText = BattleTextLine.Missing;
        return false;
    }

    /// <summary>Builds one root menu line from the command text of the catalogue.</summary>
    /// <param name="command">Command index, 0..5 in <see cref="BattleCommand"/> order.</param>
    /// <param name="enabled">Whether the command can be picked, greyed otherwise.</param>
    /// <returns>The line, with missing text when no catalogue is set.</returns>
    private BattleMenuLine Line(int command, bool enabled)
    {
        var line = Text?.Command(command) ?? BattleTextLine.Missing;
        return new BattleMenuLine(line.Debug, enabled, line);
    }

    /// <summary>Replaces the message with a catalogue message and shows it when the catalogue has the key.</summary>
    /// <param name="key">Message key of the battle text catalogue, for example <c>technique</c>.</param>
    /// <param name="substitutions">Token streams inserted into the message, in template order (actor first).</param>
    private void SetMessage(string key, params IReadOnlyList<TextToken>[] substitutions)
    {
        MessageText = Text?.Message(key, substitutions) ?? BattleTextLine.Missing;
        Message = MessageText.Debug;
        MessageVisible = MessageText != BattleTextLine.Missing;
    }

    /// <summary>Hides the message and forgets its text and label.</summary>
    private void ClearMessage()
    {
        MessageText = BattleTextLine.Missing;
        Message = string.Empty;
        MessageVisible = false;
    }

    /// <summary>Looks up the displayed name of a fighter through <see cref="NameIds"/> and the Digimon name table.</summary>
    /// <param name="fighter">Fighter to name, may be null when the slot is empty.</param>
    /// <returns>The name tokens, or an empty list when the fighter, its name id or the catalogue is missing.</returns>
    private IReadOnlyList<TextToken> NameTokens(BattleFighter? fighter) => fighter is not null && NameIds.TryGetValue(fighter.Id, out var nameId) ? Text?.DigimonByNameId(nameId).Tokens ?? [] : [];

    /// <summary>Looks up the displayed name of an item in <see cref="ItemText"/>.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The name tokens, or an empty list when the item has no text.</returns>
    private IReadOnlyList<TextToken> ItemTokens(int itemId) => ItemText.TryGetValue(itemId, out var text) ? text.Tokens : [];
}
