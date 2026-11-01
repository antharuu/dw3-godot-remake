using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>The battle's presentation phases. The menu shows in PlayerCommand; the
/// enemy's turn is EnemyTurn; Ended is any terminal outcome (won/lost/fled).</summary>
public enum BattleFlowPhase
{
    PlayerCommand,
    EnemyTurn,
    Ended,
}

/// <summary>One technique the active partner can pick in the Technique submenu.</summary>
public sealed record BattleTechnique(int Id, string Label, TechData Tech, BattleTextLine? Text = null);

/// <summary>
/// The battle's turn flow, the thin engine-free wrapper over the BattleState that
/// the presentation drives. It owns the menu phase (player command vs enemy turn), the six
/// source commands (attack/technique/digivolve/switch/item/run), the digivolution and item
/// catalogues, and a one-line message. Every rule delegates to BattleState: damage,
/// fleeing, digivolving, switching, items and the conditional-turn scheduler all stay in the
/// simulation core; victory/defeat come from BattleState.CheckOutcome.
/// </summary>
public sealed class BattleFlowController
{
    /// <summary>The resolved basic attack (the active partner's first learned skill, source
    /// WFIGHTMN_runCommand case 0 -> FIGHTSTG_startFirstTech -> skills[0]).</summary>
    public TechData PlayerBasicTech { get; set; } = new();

    /// <summary>The enemy's basic attack (its battle table's first technique).</summary>
    public TechData EnemyBasicTech { get; set; } = new();

    /// <summary>The active partner's techniques, for the Technique submenu.</summary>
    public IReadOnlyList<BattleTechnique> PlayerTechniques { get; set; } = [];

    /// <summary>The technique catalogue (id -> resolved technique), for resolving the enemy's
    /// battle-table techniques on its turn (RunEnemyTurn).</summary>
    public IReadOnlyDictionary<int, TechData> Techs { get; set; } = new Dictionary<int, TechData>();

    /// <summary>The Digimon forms the active partner can digivolve into,
    /// for the Digivolve submenu. The host refreshes it when the active partner changes.</summary>
    public IReadOnlyList<int> PlayerDigivolutions { get; set; } = [];

    /// <summary>The battle-item catalogue, keyed by item id (BattleItemsData).</summary>
    public IReadOnlyDictionary<int, BattleItem> Items { get; set; } = new Dictionary<int, BattleItem>();

    /// <summary>The bag: item id -> how many the party holds (GAME.items). Using an item
    /// decrements it; the item menu lists only the ids with a positive count.</summary>
    public Dictionary<int, int> ItemStock { get; set; } = [];
    /// <summary>FSITMNAM token streams supplied with the decoded battle-item catalogue.</summary>
    public IReadOnlyDictionary<int, BattleTextLine> ItemText { get; set; } = new Dictionary<int, BattleTextLine>();

    /// <summary>Fighter id -> display label, for the action log.</summary>
    /// <summary>Fighter id -> source DIGNAM nameId. Enemies use BattleTableEntry.nameId and partners use DigimonData.nameId.</summary>
    public IReadOnlyDictionary<int, int> NameIds { get; set; } = new Dictionary<int, int>();
    public BattleTextCatalog? Text { get; set; }
    public BattleTextLine MessageText { get; private set; } = BattleTextLine.Missing;

    public BattleState State { get; private set; } = new();
    public BattleFlowPhase Phase { get; private set; } = BattleFlowPhase.PlayerCommand;
    public string Message { get; private set; } = "";
    /// <summary>The battle message shown below the action area. Menus never retain it.</summary>
    public bool MessageVisible { get; private set; }

    public BattleOutcome Outcome => State.Outcome;

    /// <summary>Begins a battle from an already-initialised state, on the player's turn.</summary>
    public void Start(BattleState state)
    {
        State = state;
        State.Techs = Techs;
        Phase = BattleFlowPhase.PlayerCommand;
        ClearMessage();
    }

    /// <summary>Resolves the player's command and advances the phase. <paramref name="arg"/> is
    /// the technique id (Technique), the Digimon form id (Digivolve), the partner slot (Switch)
    /// or the item id (Item), ignored for Attack and Flee. Returns false when the command was
    /// not spendable (an unaffordable technique, a barred digivolution, an invalid switch or a
    /// missing item), leaving the menu open.</summary>
    public bool Choose(BattleCommand command, int arg, BattleRng rng)
    {
        if (Phase != BattleFlowPhase.PlayerCommand) return false;
        var actor = State.ActivePlayerFighter;
        switch (command)
        {
            case BattleCommand.Attack:
                if (!State.PlayerAttack(PlayerBasicTech, rng)) return false;
                SetMessage("basicAttack", NameTokens(actor));
                break;
            case BattleCommand.Technique:
                {
                    var technique = PlayerTechniques.FirstOrDefault(candidate => candidate.Id == arg);
                    if (technique is null || !State.PlayerTechnique(technique.Tech, rng))
                    {
                        SetMessage("noMp");
                        return false;
                    }
                    SetMessage("technique", NameTokens(actor), technique.Text?.Tokens ?? []);
                    break;
                }
            case BattleCommand.Digivolve:
                if (!State.PlayerDigivolve(arg))
                {
                    MessageText = BattleTextLine.Missing;
                    return false;
                }
                SetMessage("digivolve", NameTokens(actor), NameTokens(State.ActivePlayerFighter));
                break;
            case BattleCommand.Switch:
                if (!State.PlayerSwitch(arg))
                {
                    MessageText = BattleTextLine.Missing;
                    return false;
                }
                SetMessage("switch", NameTokens(State.ActivePlayerFighter));
                break;
            case BattleCommand.Item:
                if (!UseItem(arg))
                {
                    MessageText = BattleTextLine.Missing;
                    return false;
                }
                SetMessage("item", NameTokens(actor), ItemTokens(arg));
                break;
            case BattleCommand.Flee:
                {
                    var fled = State.PlayerFlee(rng);
                    if (fled) SetMessage("fledPlayer", NameTokens(actor));
                    break;
                }
        }
        AdvancePhase();
        return true;
    }

    /// <summary>Consumes one of <paramref name="itemId"/> from the bag and applies its effect
    /// (UseItem): the item must be in the catalogue and the stock positive.</summary>
    private bool UseItem(int itemId)
    {
        if (!Items.TryGetValue(itemId, out var item)) return false;
        if (!ItemStock.TryGetValue(itemId, out var count) || count <= 0) return false;
        if (!State.PlayerItem(item)) return false;
        ItemStock[itemId] = count - 1;
        return true;
    }

    /// <summary>Resolves the enemy's turn: it decides its action from its battle table
    /// (attack/technique/switch/run-away; its counter-attack fires inside the player's hit) and
    /// executes it, then advances the phase.</summary>
    public void EnemyAct(BattleRng rng)
    {
        if (Phase != BattleFlowPhase.EnemyTurn) return;
        var decision = State.RunEnemyTurn(rng);
        var enemy = State.ActiveEnemyFighter;
        SetMessage(decision.Kind is EnemyActionKind.Technique1 or EnemyActionKind.Technique2 ? "technique" : "actor", NameTokens(enemy));
        AdvancePhase();
    }

    /// <summary>
    /// The six-command root menu's lines, greyed by the active fighter's state
    /// The six-command root menu lines, greyed by the active fighter's state:
    /// attack/technique/digivolve are greyed while
    /// asleep, digivolve while FIGHTER_NO_DIGIVOLVE, and everything but Switch once the
    /// active partner is knocked out (the manual Switch is the only command left then).
    /// </summary>
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

    private void AdvancePhase()
    {
        if (State.Outcome != BattleOutcome.None)
        {
            if (State.Outcome == BattleOutcome.Won) SetMessage("won");
            else if (State.Outcome == BattleOutcome.Lost) SetMessage("lost");
            Phase = BattleFlowPhase.Ended;
            return;
        }
        Phase = State.NextActor == BattleSide.Player ? BattleFlowPhase.PlayerCommand : BattleFlowPhase.EnemyTurn;
        if (Phase == BattleFlowPhase.PlayerCommand) ClearMessage();
    }

    /// <summary>Force-ends the battle with the given outcome: the test driver sets the battle's result directly,
    /// so a defeat can be exercised without a contrived fight. Production reaches Ended through
    /// <see cref="AdvancePhase"/> after the outcome check fires.</summary>
    public void EndBattle(BattleOutcome outcome)
    {
        State.Outcome = outcome;
        Phase = BattleFlowPhase.Ended;
    }

    private BattleMenuLine Line(int command, bool enabled) { var line = Text?.Command(command) ?? BattleTextLine.Missing; return new BattleMenuLine(line.Debug, enabled, line); }
    private void SetMessage(string key, params IReadOnlyList<TextToken>[] substitutions) { MessageText = Text?.Message(key, substitutions) ?? BattleTextLine.Missing; Message = MessageText.Debug; MessageVisible = MessageText != BattleTextLine.Missing; }
    private void ClearMessage() { MessageText = BattleTextLine.Missing; Message = ""; MessageVisible = false; }
    private IReadOnlyList<TextToken> NameTokens(BattleFighter? fighter) => fighter is not null && NameIds.TryGetValue(fighter.Id, out var nameId) ? Text?.DigimonByNameId(nameId).Tokens ?? [] : [];
    private IReadOnlyList<TextToken> ItemTokens(int itemId) => ItemText.TryGetValue(itemId, out var text) ? text.Tokens : [];


}
