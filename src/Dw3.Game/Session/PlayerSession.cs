using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>Owns the in-memory player state shared by consecutive battles until a future save system persists it.</summary>
public sealed class PlayerSession
{
    /// <summary>The most money the source game lets the player hold; a larger total is clamped to it.</summary>
    public const int MoneyMax = 9_999_999;

    /// <summary>The most copies of one item the source game keeps; an award beyond it is lost.</summary>
    public const int ItemMax = 99;

    /// <summary>Initializes a new instance of the <see cref="PlayerSession"/> class.</summary>
    /// <param name="partners">Persistent party slots. At most three slots are retained.</param>
    /// <param name="inventory">Item quantities owned by the player.</param>
    /// <param name="money">Money owned by the player, 0 to <see cref="MoneyMax"/>.</param>
    public PlayerSession(IReadOnlyList<PlayerSessionPartner> partners, IReadOnlyDictionary<int, int> inventory, int money = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(money);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(money, MoneyMax);
        ArgumentNullException.ThrowIfNull(partners);
        ArgumentNullException.ThrowIfNull(inventory);
        Partners = new PlayerSessionPartner?[3];
        for (var index = 0; index < Partners.Length && index < partners.Count; index++)
        {
            Partners[index] = partners[index];
        }

        Inventory = new Dictionary<int, int>(inventory);
        Money = money;
    }

    /// <summary>Gets the session-owned party slots.</summary>
    public PlayerSessionPartner?[] Partners { get; }

    /// <summary>Gets the session-owned item quantities. Battle menus list only the ids of their battle item catalogue.</summary>
    public Dictionary<int, int> Inventory { get; }

    /// <summary>Gets the money the player owns, 0 to <see cref="MoneyMax"/>.</summary>
    public int Money { get; private set; }

    /// <summary>Gets each party slot's persistent growth state, null for an empty slot or a partner without growth.</summary>
    public IReadOnlyList<PartnerGrowthState?> PartyGrowth => [.. Partners.Select(partner => partner?.Growth)];

    /// <summary>Creates the explicit debug session used until field-owned party loading exists.</summary>
    /// <param name="fighters">Decoded fighter data supplying the debug party's base sources.</param>
    /// <param name="items">Decoded battle items receiving the debug stock.</param>
    /// <returns>One session whose debug defaults are created once rather than per battle.</returns>
    public static PlayerSession CreateDebug(FighterData fighters, BattleItemsData items)
    {
        IReadOnlyList<int> ids = BattleSetupFactory.StarterPartnerDigimonIds(fighters);
        PartnerSeed?[] seeds = BattleSetupFactory.BuildParty(fighters, TestTeamGrowth.Level, ids);
        IReadOnlyList<PartnerGrowthState> growth = TestTeamGrowth.Create(ids, fighters);
        var partners = new List<PlayerSessionPartner>();
        for (int index = 0; index < seeds.Length; index++)
        {
            if (seeds[index] is PartnerSeed seed)
            {
                var partner = new PlayerSessionPartner(seed, [seed.Id], [], [], [], growth[index]);
                if (fighters.Digimon.TryGetValue(seed.Id, out DigimonData? data))
                {
                    PartnerFormProgression.InitializeInherentSkills(partner.FormProgress(seed.Id), data);
                }

                partners.Add(partner);
            }
        }

        return new PlayerSession(partners, items.Items.ToDictionary(item => item.Id, _ => 3));
    }

    /// <summary>Adds a reward to the player's money, clamped to the source maximum.</summary>
    /// <param name="amount">Money won, 0 or more.</param>
    /// <returns>The amount actually added after the clamp.</returns>
    public int CreditMoney(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        int before = Money;
        Money = (int)Math.Min((long)Money + amount, MoneyMax);
        return Money - before;
    }

    /// <summary>Adds one copy of a won item, kept at the source's 99-copy maximum.</summary>
    /// <param name="itemId">Item id, 0 for no item.</param>
    /// <returns>True when the stock grew, false for no item or a full stock.</returns>
    public bool CreditItem(int itemId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemId);
        if (itemId == 0)
        {
            return false;
        }

        int before = Inventory.GetValueOrDefault(itemId);
        Inventory[itemId] = Math.Min(before + 1, ItemMax);
        return Inventory[itemId] > before;
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

        BattleBlastResolver.EndTemporaryForm(fighter);
        partner.BattleSeed.Hp = fighter.Hp;
        partner.BattleSeed.Mp = fighter.Mp;
        partner.BattleSeed.Id = fighter.Id;
        partner.BattleSeed.BlastGauge = fighter.BlastGauge;
    }
}
