namespace Dw3Production.Simulation;

/// <summary>
/// Field encounter triggering: the step countdown, the per-area battle-rate and the random
/// draw that picks one of eight slot battles. Engine-free:
/// no Godot types. Callers feed the raw FIELD_MAP_AREAS cell value at the actor's tile (0 =
/// no area, 1..4 = battle area) and the field-busy flag; the controller owns the step
/// countdown, the per-area rate and the random draws.
/// </summary>
public sealed class EncounterController
{
    /// <summary>FIELDSTG_battleRates: how much each BattleList.Count lowers the step countdown.</summary>
    public static readonly int[] BattleRates = [2000, 3, 4, 6, 9, 18];

    private readonly IEncounterRng _rng;
    private BattleList[]? _battles;

    /// <summary>Initializes a new instance of the <see cref="EncounterController"/> class.</summary>
    /// <param name="rng">The random table source; must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rng"/> is null.</exception>
    public EncounterController(IEncounterRng rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
    }

    /// <summary>Gets or sets gAME.battleSteps: steps remaining until the next field battle.</summary>
    public int BattleSteps { get; set; }

    /// <summary>Gets or sets a value indicating whether bATTLE_SETUP.randomBattles: when false a roll-out only re-rolls, it starts nothing.</summary>
    public bool RandomBattles { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field-busy guard. True when any of FIELDSTG_state.busy/acting/frozen/battleStarting
    /// is set, which suppresses counting entirely.
    /// </summary>
    public bool Busy { get; set; }

    /// <summary>
    /// Loads the four battle area lists. Replaces any previously loaded set.
    /// </summary>
    /// <param name="battles">Exactly four battle lists, one per battle area.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="battles"/> does not have exactly four entries.</exception>
    public void SetBattles(IReadOnlyList<BattleList> battles)
    {
        if (battles.Count != 4)
        {
            throw new ArgumentException("a FieldBattles holds four battle areas", nameof(battles));
        }

        _battles = [.. battles];
    }

    /// <summary>FIELDSTG_rollBattleSteps: roll the steps to the next battle from the RNG.</summary>
    public void RollBattleSteps()
    {
        var value = _rng.Draw() % 2304;
        // Under 0x100 the raw value is the countdown (0..255); otherwise (value + 0x100) / 2
        // yields 256..1279. The two branches meet at 256 with no gap and no overlap, so the
        // countdown always lands in 0..1279 (source header: "under 0x100, or 0x100 to 0x4FF").
        BattleSteps = value < 0x100 ? value : (value + 0x100) / 2;
    }

    /// <summary>
    /// FIELDSTG_countBattleSteps: one step of the countdown while in a battle area. areaCell is
    /// the raw FIELD_MAP_AREAS value (0 = none). Returns the started area battle, or null when
    /// nothing started (skipped, still counting down, or random battles off).
    /// </summary>
    /// <param name="areaCell">Raw FIELD_MAP_AREAS cell at the actor's tile; 0 = not in a battle area.</param>
    /// <returns>The encounter that just started, or null when nothing triggered.</returns>
    public EncounterRecord? CountBattleSteps(int areaCell)
    {
        if (Busy)
        {
            return null;
        }

        if (_battles is null)
        {
            return null;         // FIELDSTG_state.battles == NULL
        }

        if (areaCell == 0)
        {
            return null;            // not in a battle area
        }

        if (areaCell is < 1 or > 4)
        {
            return null;   // defensive: source would index past battles[4]
        }

        var area = areaCell - 1;
        BattleSteps -= BattleRates[_battles[area].Count];
        if (BattleSteps <= 0)
        {
            EncounterRecord? record = null;
            if (RandomBattles)
            {
                record = StartAreaBattle(area);
            }

            RollBattleSteps();
            return record;
        }

        return null;
    }

    /// <summary>FIELDSTG_startAreaBattle: pick one of the area's eight battles at random.</summary>
    /// <param name="area">Zero-based battle area index (0..3).</param>
    /// <returns>The encounter record for the randomly selected battle slot.</returns>
    /// <exception cref="InvalidOperationException">Thrown when battle data has not been loaded.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="area"/> is outside 0..3.</exception>
    public EncounterRecord StartAreaBattle(int area)
    {
        if (_battles is null)
        {
            throw new InvalidOperationException("battle table not loaded before area battle");
        }

        if (area is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(area));
        }

        var index = _rng.Draw() & 7;
        return new EncounterRecord("area", area, index, _battles[area].Battles[index]);
    }

    /// <summary>FIELDSTG_startEventBattle: start battle index of the fourth area (3).</summary>
    /// <param name="index">Battle slot index (0..7).</param>
    /// <returns>The encounter record, or null when battle data has not been loaded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is outside 0..7.</exception>
    public EncounterRecord? StartEventBattle(int index)
    {
        if (_battles is null)
        {
            return null;         // source guards FIELDSTG_state.battles != NULL
        }

        if (index is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new EncounterRecord("event", 3, index, _battles[3].Battles[index]);
    }
}
