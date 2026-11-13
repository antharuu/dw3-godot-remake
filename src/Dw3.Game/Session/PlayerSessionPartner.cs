using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>Stores one persistent party slot and the data the battle setup must not reconstruct from debug defaults.</summary>
public sealed class PlayerSessionPartner
{
    private readonly Dictionary<int, BattleStats> _authoredFormStats;

    /// <summary>Initializes a new instance of the <see cref="PlayerSessionPartner"/> class.</summary>
    /// <param name="battleSeed">Canonical resources and stat sources shared with battle fighters.</param>
    /// <param name="knownForms">Digimon forms the partner owns.</param>
    /// <param name="equippedForms">Up to three acquired forms selected for the normal Digivolution menu.</param>
    /// <param name="knownTechniques">Technique ids the partner knows.</param>
    /// <param name="equipmentIds">Equipped item ids affecting battle resolution.</param>
    /// <param name="growth">Persistent level, experience and grown totals, or null when the partner does not grow.</param>
    public PlayerSessionPartner(
        PartnerSeed battleSeed,
        IReadOnlyList<int> knownForms,
        IReadOnlyList<int> equippedForms,
        IReadOnlyList<int> knownTechniques,
        IReadOnlyList<int> equipmentIds,
        PartnerGrowthState? growth = null)
    {
        BattleSeed = battleSeed ?? throw new ArgumentNullException(nameof(battleSeed));
        OriginFormId = BattleSeed.Id;
        KnownForms = [.. knownForms ?? throw new ArgumentNullException(nameof(knownForms))];
        EquippedForms = ValidateEquippedForms(equippedForms);
        KnownTechniques = [.. knownTechniques ?? throw new ArgumentNullException(nameof(knownTechniques))];
        EquipmentIds = [.. equipmentIds ?? throw new ArgumentNullException(nameof(equipmentIds))];
        Growth = growth;
        _authoredFormStats = new Dictionary<int, BattleStats>(BattleSeed.FormStats);
    }

    /// <summary>Gets the canonical seed containing the partner's current HP, MP, stats and persistent statuses.</summary>
    public PartnerSeed BattleSeed { get; }

    /// <summary>Gets the source-tree base form used to evaluate this partner's normal Digivolution entries.</summary>
    public int OriginFormId { get; }

    /// <summary>Gets the Digimon forms the partner owns for battle menu eligibility.</summary>
    public IReadOnlyList<int> KnownForms { get; }

    /// <summary>Gets the explicitly equipped normal Digivolution forms in source-slot order.</summary>
    public IReadOnlyList<int> EquippedForms { get; }

    /// <summary>Gets the techniques the partner knows outside the current Digimon table defaults.</summary>
    public IReadOnlyList<int> KnownTechniques { get; }

    /// <summary>Gets the equipped item ids consumed by battle stat resolution.</summary>
    public IReadOnlyList<int> EquipmentIds { get; }

    /// <summary>Gets the persistent level, experience and grown totals that battle rewards raise, or null when the partner does not grow.</summary>
    public PartnerGrowthState? Growth { get; }

    /// <summary>Carries the growth gained since a snapshot into the battle seed the next battle starts from.</summary>
    /// <remarks>The gains are added rather than copied so that seed values not owned by growth, such as
    /// equipment contributions, are kept. Current HP and MP are unchanged, as in the source level-up.
    /// Form sources derived from the old totals are dropped so the next setup derives them again.</remarks>
    /// <param name="before">Growth state captured before the reward was applied.</param>
    /// <returns>True when a level, a maximum or a stat changed in the seed.</returns>
    public bool ApplyGrowthSince(PartnerGrowthState before)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (Growth is null || Growth.Level == before.Level)
        {
            return false;
        }

        BattleStats stats = BattleSeed.Stats;
        BattleSeed.MaxHp += Growth.MaxHp - before.MaxHp;
        BattleSeed.MaxMp += Growth.MaxMp - before.MaxMp;
        stats.Level = Growth.Level;
        for (int index = 0; index < stats.Stats.Length && index < Growth.BattleStats.Length; index++)
        {
            stats.Stats[index] += Growth.BattleStats[index] - before.BattleStats[index];
        }

        for (int index = 0; index < Growth.Resistances.Length && index < stats.Resist.Length; index++)
        {
            stats.Resist[index] += Growth.Resistances[index] - before.Resistances[index];
        }

        BattleSeed.FormStats = new Dictionary<int, BattleStats>(_authoredFormStats);
        return true;
    }

    /// <summary>Validates explicit source-equivalent normal Digivolution slots.</summary>
    /// <param name="equippedForms">Forms assigned to the three selectable slots.</param>
    /// <returns>A copied, validated slot list preserving its explicit order.</returns>
    private IReadOnlyList<int> ValidateEquippedForms(IReadOnlyList<int> equippedForms)
    {
        ArgumentNullException.ThrowIfNull(equippedForms);
        if (equippedForms.Count > 3)
        {
            throw new ArgumentException("A partner can equip at most three Digivolution forms.", nameof(equippedForms));
        }

        var acquired = new HashSet<int>(KnownForms) { OriginFormId };
        if (equippedForms.Any(form => form == 0 || !acquired.Contains(form)))
        {
            throw new ArgumentException("Every equipped Digivolution form must be acquired.", nameof(equippedForms));
        }

        if (equippedForms.Distinct().Count() != equippedForms.Count)
        {
            throw new ArgumentException("A Digivolution form cannot occupy multiple slots.", nameof(equippedForms));
        }

        return [.. equippedForms];
    }
}
