namespace Dw3Production.Simulation;

/// <summary>
/// One battle of a field area's BattleList (field_map.h AreaBattle). Encounter is the
/// FIELDSTG_encounters index, Stage is the fight stage, Music is the battle music id.
/// The empty slots keep the bare { 0, 0, MUSIC(1, 0) }.
/// </summary>
public sealed record AreaBattle(int Encounter, int Stage, int Music);

/// <summary>
/// The eight battles of one field area, picked from at random (field_map.h BattleList).
/// Count indexes FIELDSTG_battleRates: how much each walk step lowers the countdown.
/// </summary>
public sealed class BattleList
{
    public BattleList(int count, IReadOnlyList<AreaBattle> battles)
    {
        if (battles.Count != 8) throw new ArgumentException("a BattleList holds exactly eight battles", nameof(battles));
        Count = count;
        Battles = battles;
    }

    public int Count { get; }
    public IReadOnlyList<AreaBattle> Battles { get; }
}

/// <summary>
/// A field encounter the controller resolved. Kind is "area" (FIELDSTG_startAreaBattle) or
/// "event" (FIELDSTG_startEventBattle); Area is the zero-based battle area (always 3 for
/// events); Index is the battle slot 0..7.
/// </summary>
public sealed record EncounterRecord(string Kind, int Area, int Index, AreaBattle Battle);

/// <summary>
/// The injectable source RNG. Next() returns the next RANDOM_TABLE entry, 0..RANDOM_COUNT-1
/// (0..4095) from a pseudo-random table of 4096 entries. A deterministic implementation
/// makes the controller testable bit-for-bit.
/// </summary>
public interface EncounterRng
{
    int Next();
}

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

    private readonly EncounterRng _rng;
    private BattleList[]? _battles;

    public EncounterController(EncounterRng rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
    }

    /// <summary>GAME.battleSteps: steps remaining until the next field battle.</summary>
    public int BattleSteps { get; set; }

    /// <summary>BATTLE_SETUP.randomBattles: when false a roll-out only re-rolls, it starts nothing.</summary>
    public bool RandomBattles { get; set; }

    /// <summary>
    /// The field-busy guard. True when any of FIELDSTG_state.busy/acting/frozen/battleStarting
    /// is set, which suppresses counting entirely.
    /// </summary>
    public bool Busy { get; set; }

    public void SetBattles(IReadOnlyList<BattleList> battles)
    {
        if (battles.Count != 4) throw new ArgumentException("a FieldBattles holds four battle areas", nameof(battles));
        _battles = [.. battles];
    }

    /// <summary>FIELDSTG_rollBattleSteps: roll the steps to the next battle from the RNG.</summary>
    public void RollBattleSteps()
    {
        var value = _rng.Next() % 2304;
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
    public EncounterRecord? CountBattleSteps(int areaCell)
    {
        if (Busy) return null;
        if (_battles is null) return null;         // FIELDSTG_state.battles == NULL
        if (areaCell == 0) return null;            // not in a battle area
        if (areaCell is < 1 or > 4) return null;   // defensive: source would index past battles[4]
        var area = areaCell - 1;
        BattleSteps -= BattleRates[_battles[area].Count];
        if (BattleSteps <= 0)
        {
            EncounterRecord? record = null;
            if (RandomBattles) record = StartAreaBattle(area);
            RollBattleSteps();
            return record;
        }
        return null;
    }

    /// <summary>FIELDSTG_startAreaBattle: pick one of the area's eight battles at random.</summary>
    public EncounterRecord StartAreaBattle(int area)
    {
        if (_battles is null) throw new InvalidOperationException("battle table not loaded before area battle");
        if (area is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(area));
        var index = _rng.Next() & 7;
        return new EncounterRecord("area", area, index, _battles[area].Battles[index]);
    }

    /// <summary>FIELDSTG_startEventBattle: start battle index of the fourth area (3).</summary>
    public EncounterRecord? StartEventBattle(int index)
    {
        if (_battles is null) return null;         // source guards FIELDSTG_state.battles != NULL
        if (index is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(index));
        return new EncounterRecord("event", 3, index, _battles[3].Battles[index]);
    }
}
