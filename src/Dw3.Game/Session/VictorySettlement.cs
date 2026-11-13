using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>
/// The single authority that credits a won battle's awards to the session. The battle's end only snapshots the
/// awards, the report then commits each stage through the callbacks of <see cref="IFightReportSettlement"/>, every
/// stage at most once, so repeated or held input, report return and the next battle can never credit twice.
/// </summary>
public sealed class VictorySettlement : IFightReportSettlement
{
    private readonly PlayerSession _session;
    private readonly BattleVictoryContext _victory;
    private readonly IReadOnlyList<int>[] _usedSlots;
    private readonly int _globalUsed;
    private readonly Dictionary<int, FightReportPartner> _raised = [];

    /// <summary>Initializes a new instance of the <see cref="VictorySettlement"/> class.</summary>
    /// <param name="session">Persistent player state that receives the awards.</param>
    /// <param name="victory">Encounter reward, decoded data and the shared random stream consumed by level-ups.</param>
    /// <param name="snapshot">Award snapshot whose partner rows carry the pre-raise display.</param>
    /// <param name="usedSlots">Each report partner's used equipped slots, captured at the battle's end.</param>
    internal VictorySettlement(PlayerSession session, BattleVictoryContext victory, FightReportInput snapshot, IReadOnlyList<int>[] usedSlots)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _victory = victory ?? throw new ArgumentNullException(nameof(victory));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _usedSlots = usedSlots ?? throw new ArgumentNullException(nameof(usedSlots));
        _globalUsed = usedSlots.Sum(slots => slots.Count);
    }

    /// <inheritdoc/>
    public FightReportInput Snapshot { get; }

    /// <summary>Gets the award snapshot with every raised partner's row replaced by its settled row.</summary>
    public FightReportInput Settled => Snapshot with
    {
        Partners = [.. Snapshot.Partners.Select(partner => _raised.GetValueOrDefault(partner.PartyIndex, partner))],
    };

    /// <summary>Gets a value indicating whether the money award was credited.</summary>
    public bool MoneyCredited { get; private set; }

    /// <summary>Gets a value indicating whether the item award stage ran.</summary>
    public bool ItemCredited { get; private set; }

    /// <summary>Tests whether one party slot's raise was committed.</summary>
    /// <param name="partyIndex">Party slot.</param>
    /// <returns>True when that partner's experience and forms were credited.</returns>
    public bool IsRaised(int partyIndex) => _raised.ContainsKey(partyIndex);

    /// <inheritdoc/>
    public FightReportPartner RaisePartner(FightReportPartner snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_raised.TryGetValue(snapshot.PartyIndex, out FightReportPartner? settled))
        {
            return settled;
        }

        int row = IndexOf(snapshot.PartyIndex);
        PlayerSessionPartner? partner = PartnerAt(snapshot.PartyIndex);
        settled = partner is null || snapshot.ExpGained <= 0 ? snapshot : Raise(partner, Snapshot.Partners[row], _usedSlots[row]);
        _raised.Add(snapshot.PartyIndex, settled);
        return settled;
    }

    /// <inheritdoc/>
    public void CreditMoney()
    {
        if (MoneyCredited)
        {
            return;
        }

        MoneyCredited = true;
        _session.CreditMoney(Snapshot.Money);
    }

    /// <inheritdoc/>
    public void CreditItem()
    {
        if (ItemCredited)
        {
            return;
        }

        ItemCredited = true;
        _session.CreditItem(Snapshot.Item);
    }

    /// <summary>
    /// Commits every stage the report would reach, in its order, for a caller that resolves a victory without playing
    /// the report. Stages already committed are not repeated, and a partner without experience is skipped as the
    /// report skips it.
    /// </summary>
    public void CommitAll()
    {
        foreach (FightReportPartner partner in Snapshot.Partners.Where(partner => partner.ExpGained > 0))
        {
            RaisePartner(partner);
        }

        CreditMoney();
        CreditItem();
    }

    /// <summary>Applies the partner's experience and growth, then its used-form progression and unlocks.</summary>
    /// <param name="partner">Persistent partner.</param>
    /// <param name="snapshot">Its award row.</param>
    /// <param name="used">Its used equipped slots.</param>
    /// <returns>The settled report row.</returns>
    private FightReportPartner Raise(PlayerSessionPartner partner, FightReportPartner snapshot, IReadOnlyList<int> used)
    {
        PartnerGrowthState? before = partner.Growth?.Copy();
        BattleOutcomeResolver.ApplyPartnerGrowth(_victory.GrowthTables, partner.Growth, BaseDigimon(partner), snapshot.ExpGained, _victory.Random);
        if (before is not null)
        {
            partner.ApplyGrowthSince(before);
        }

        FightReportPartner grown = snapshot with { LevelAfter = partner.Growth?.Level ?? snapshot.LevelBefore };
        return BattleFormSettlement.ApplyPartner(partner, _victory, grown, used, _globalUsed);
    }

    /// <summary>Returns the base Digimon record whose growth curve the partner levels with.</summary>
    /// <param name="partner">Persistent partner.</param>
    /// <returns>The partner's base form record.</returns>
    /// <exception cref="InvalidOperationException">The base form is not decoded.</exception>
    private DigimonData BaseDigimon(PlayerSessionPartner partner) =>
        _victory.Fighters.Digimon.TryGetValue(partner.OriginFormId, out DigimonData? digimon)
            ? digimon
            : throw new InvalidOperationException($"base form {partner.OriginFormId} is not decoded");

    /// <summary>Finds a party slot's row in the snapshot.</summary>
    /// <param name="partyIndex">Party slot.</param>
    /// <returns>The row index.</returns>
    /// <exception cref="ArgumentException">The snapshot has no row for that slot.</exception>
    private int IndexOf(int partyIndex)
    {
        for (int index = 0; index < Snapshot.Partners.Count; index++)
        {
            if (Snapshot.Partners[index].PartyIndex == partyIndex)
            {
                return index;
            }
        }

        throw new ArgumentException($"no report row for party slot {partyIndex}", nameof(partyIndex));
    }

    /// <summary>Returns the partner at one valid party index.</summary>
    /// <param name="index">Party slot.</param>
    /// <returns>The partner, or null for an invalid or empty slot.</returns>
    private PlayerSessionPartner? PartnerAt(int index) => index >= 0 && index < _session.Partners.Length ? _session.Partners[index] : null;
}
