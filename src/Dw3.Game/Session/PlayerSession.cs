using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>Owns the in-memory player state shared by consecutive battles until a future save system persists it.</summary>
public sealed class PlayerSession
{
    /// <summary>Initializes a new instance of the <see cref="PlayerSession"/> class.</summary>
    /// <param name="partners">Persistent party slots. At most three slots are retained.</param>
    /// <param name="inventory">Battle item quantities owned by the player.</param>
    public PlayerSession(IReadOnlyList<PlayerSessionPartner> partners, IReadOnlyDictionary<int, int> inventory)
    {
        ArgumentNullException.ThrowIfNull(partners);
        ArgumentNullException.ThrowIfNull(inventory);
        Partners = new PlayerSessionPartner?[3];
        for (var index = 0; index < Partners.Length && index < partners.Count; index++)
        {
            Partners[index] = partners[index];
        }

        Inventory = new Dictionary<int, int>(inventory);
    }

    /// <summary>Gets the session-owned party slots.</summary>
    public PlayerSessionPartner?[] Partners { get; }

    /// <summary>Gets the session-owned battle item quantities.</summary>
    public Dictionary<int, int> Inventory { get; }

    /// <summary>Creates the explicit debug session used until field-owned party loading exists.</summary>
    /// <param name="fighters">Decoded fighter data supplying the debug party's base sources.</param>
    /// <param name="items">Decoded battle items receiving the debug stock.</param>
    /// <returns>One session whose debug defaults are created once rather than per battle.</returns>
    public static PlayerSession CreateDebug(FighterData fighters, BattleItemsData items)
    {
        IReadOnlyList<int> ids = BattleSetupFactory.StarterPartnerDigimonIds(fighters);
        PartnerSeed?[] seeds = BattleSetupFactory.BuildParty(fighters, TestTeamGrowth.Level, ids);
        var partners = new List<PlayerSessionPartner>();
        foreach (PartnerSeed seed in seeds.OfType<PartnerSeed>())
        {
            partners.Add(new PlayerSessionPartner(seed, [seed.Id], [], [], []));
        }

        return new PlayerSession(partners, items.Items.ToDictionary(item => item.Id, _ => 3));
    }

    /// <summary>Builds the canonical battle seeds without recreating resources or persistent status arrays.</summary>
    /// <returns>Three party slots sharing the session-owned seeds.</returns>
    public PartnerSeed?[] BuildParty()
    {
        var party = new PartnerSeed?[Partners.Length];
        for (var index = 0; index < Partners.Length; index++)
        {
            party[index] = Partners[index]?.BattleSeed;
        }

        return party;
    }

    /// <summary>Copies mutable battle resources back to their corresponding session-owned partner sources.</summary>
    /// <param name="fighters">Battle fighters occupying the player party slots.</param>
    public void CommitBattle(IReadOnlyList<BattleFighter?> fighters)
    {
        ArgumentNullException.ThrowIfNull(fighters);
        for (var index = 0; index < Partners.Length && index < fighters.Count; index++)
        {
            CommitPartner(Partners[index], fighters[index]);
        }
    }

    /// <summary>Copies a single fighter's mutable resources to its session source when both slots exist.</summary>
    /// <param name="partner">Session partner receiving the resources.</param>
    /// <param name="fighter">Battle fighter that owns the latest resources.</param>
    private static void CommitPartner(PlayerSessionPartner? partner, BattleFighter? fighter)
    {
        if (partner is null || fighter is null)
        {
            return;
        }

        partner.BattleSeed.Hp = fighter.Hp;
        partner.BattleSeed.Mp = fighter.Mp;
        partner.BattleSeed.Id = fighter.Temporary ? fighter.PrevId : fighter.Id;
        partner.BattleSeed.BlastGauge = 0;
    }
}
