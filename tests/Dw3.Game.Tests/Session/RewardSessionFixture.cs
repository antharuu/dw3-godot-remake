using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests.Session;

/// <summary>Synthetic session, encounter and growth data for reward settlement through the production battle seams.</summary>
internal static class RewardSessionFixture
{
    /// <summary>Item id dropped by the synthetic enemy.</summary>
    public const int DropItem = 7;

    /// <summary>Item id used by forced-prize scenarios.</summary>
    public const int PrizeItem = 9;

    /// <summary>Healing item already owned by the session.</summary>
    public const int HealItem = 42;

    /// <summary>Builds a three-partner session whose items are known before the battle.</summary>
    /// <param name="inventory">Initial item quantities.</param>
    /// <returns>A session whose partners all start at level one.</returns>
    public static PlayerSession CreateSession(IReadOnlyDictionary<int, int> inventory) =>
        new([Partner(1), Partner(2), Partner(3)], inventory);

    /// <summary>Builds one persistent partner slot whose battle stats match its base Digimon record.</summary>
    /// <param name="id">Base Digimon id.</param>
    /// <param name="equipment">Equipped item ids.</param>
    /// <returns>A partner at level one with full resources.</returns>
    public static PlayerSessionPartner Partner(int id, IReadOnlyList<int>? equipment = null) =>
        new(Seed(id), [id], [], [1, 2], equipment ?? [], Growth());

    /// <summary>Builds the level-one growth state matching <see cref="Seed(int)"/>.</summary>
    /// <returns>Growth state with no experience.</returns>
    public static PartnerGrowthState Growth() => new()
    {
        Level = 1,
        MaxHp = 100,
        MaxMp = 10,
        BattleStats = [10, 10, 10, 10, 100, 5],
    };

    /// <summary>Builds a level-one battle seed matching <see cref="Digimon(int)"/>.</summary>
    /// <param name="id">Base Digimon id.</param>
    /// <returns>Seed with full HP and MP.</returns>
    public static PartnerSeed Seed(int id)
    {
        var stats = new BattleStats { Level = 1 };
        int[] values = [10, 10, 10, 10, 100];
        for (int index = 0; index < values.Length; index++)
        {
            stats.Stats[index] = values[index];
        }

        return new PartnerSeed { Id = id, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = stats };
    }

    /// <summary>Builds a started production flow for the synthetic encounter.</summary>
    /// <param name="session">Session reused by consecutive battles.</param>
    /// <param name="item">Item the enemy carries, 0 for none.</param>
    /// <returns>A session-backed flow.</returns>
    public static BattleFlowController Build(PlayerSession session, int item = DropItem) =>
        GameHost.BattleSetupCoordinator.BuildForSession(
            new GameHost.BattleSetupCoordinator.BattleSessionBuildInput(
                Fighters(item),
                Techniques(),
                Encounters(),
                1,
                session,
                Items(),
                Text(),
                new FixedRng()));

    /// <summary>Wins the battle with the active partner's lethal technique.</summary>
    /// <param name="flow">Started production flow.</param>
    public static void Win(BattleFlowController flow)
    {
        if (!flow.Choose(BattleCommand.Technique, 2, new FixedRng()) || flow.Outcome != BattleOutcome.Won)
        {
            throw new InvalidOperationException($"fixture battle did not end in victory: {flow.Outcome}");
        }
    }

    /// <summary>Builds the fighter catalogue: three partners and one enemy carrying a certain drop.</summary>
    /// <param name="item">Item the enemy carries, 0 for none.</param>
    /// <returns>Fighter data.</returns>
    public static FighterData Fighters(int item = DropItem)
    {
        DigimonData[] partners = [Digimon(1), Digimon(2), Digimon(3)];
        return new FighterData(
            partners.ToDictionary(partner => partner.Id),
            partners,
            [new BattleTableEntry(100, item, 1023, 100, [1, 0, 0], [1, 1, 1, 1, 1], new int[12], 0, [new BattleAction(1, 0, 0)], new BattleAction(0, 0, 0))]);
    }

    /// <summary>Builds a decoded partner record whose growth curve is easy to follow by hand.</summary>
    /// <param name="id">Digimon id.</param>
    /// <returns>Partner data with exp rate 10, HP growth 5 and MP growth 1.</returns>
    public static DigimonData Digimon(int id) => new(
        id,
        [10, 10, 10, 10, 100, 5],
        new int[7],
        [1, 2],
        0,
        new int[5],
        [],
        [],
        0,
        0,
        10,
        100,
        10,
        5,
        1,
        new int[6],
        new int[7],
        [],
        id,
        0,
        false);

    /// <summary>Builds growth tables with constant gains so each level is predictable.</summary>
    /// <returns>Tables giving +7 max HP, +3 max MP, +3 per stat and +1 per resistance per level.</returns>
    public static GrowthTables GrowthTables()
    {
        var tables = new GrowthTables
        {
            LevelExp = [0, 0, 0, 0],
            VitalCuts = [0, 0, 0, 0],
            VitalRandom = [2, 2, 2, 2, 2, 2, 2, 2, 2],
            ResistGains = [1, 1, 1, 1, 1, 1, 1, 1],
        };
        foreach (int[] row in tables.StatGains)
        {
            Array.Fill(row, 3);
        }

        return tables;
    }

    /// <summary>Builds the one-enemy encounter.</summary>
    /// <returns>Encounter data.</returns>
    public static EncounterData Encounters() => new([], [new Dw3Production.Conversion.BattleEnemy(1, 100, 1, 1, 0, 16)], [new EncounterEntry(1, [1], [100], 0, 0, new int[12])], new Dictionary<string, EncounterStage>());

    /// <summary>Builds the basic attack and the lethal technique.</summary>
    /// <returns>Technique data.</returns>
    public static TechniqueData Techniques() => new(new Dictionary<int, Dw3Production.Conversion.TechData> { [1] = Tech(0, 1), [2] = Tech(3, 1000) });

    /// <summary>Builds the item catalogue containing the owned, dropped and prize items.</summary>
    /// <returns>Battle item data.</returns>
    public static BattleItemsData Items() => new(
    [
        new BattleItemData(HealItem, 0, [], 0, 0, 20, 0, 0, 0, 0, "heal", "partner"),
        new BattleItemData(DropItem, 0, [], 0, 0, 20, 0, 0, 0, 0, "heal", "partner"),
        new BattleItemData(PrizeItem, 0, [], 0, 0, 20, 0, 0, 0, 0, "heal", "partner"),
    ]);

    /// <summary>Builds an empty decoded text catalogue.</summary>
    /// <returns>Text data.</returns>
    public static BattleTextData Text() => new([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>());

    /// <summary>Builds a technique with deterministic accuracy.</summary>
    /// <param name="mp">MP cost.</param>
    /// <param name="power">Power.</param>
    /// <returns>Technique data.</returns>
    private static Dw3Production.Conversion.TechData Tech(int mp, int power) => new(0, mp, power, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    /// <summary>Returns zero for every draw, which selects the first candidate and always passes the drop chance.</summary>
    internal sealed class FixedRng : IBattleRng
    {
        /// <summary>Gets the number of values drawn.</summary>
        public int DrawCount { get; private set; }

        /// <inheritdoc/>
        public int NextValue()
        {
            DrawCount++;
            return 0;
        }
    }
}
