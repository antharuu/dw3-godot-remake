using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Verifies that the production setup and resolution seams carry one session across battles.</summary>
public sealed class BattleSessionFlowTests
{
    /// <summary>Uses production setup, catalog wiring, item use and resolution before creating the next battle.</summary>
    [Fact]
    public void ConsecutiveProductionBattles_PreserveResolvedResourcesStatusesAndItemStock()
    {
        PlayerSession session = CreateSession();
        BattleFlowController first = Build(session);

        Assert.True(first.Choose(BattleCommand.Item, 42, new FixedRng()));
        AdvanceEnemyTurn(first);
        Assert.True(first.Choose(BattleCommand.Technique, 2, new FixedRng()));
        first.State.Players[0]!.PersistentStatuses[1] = 6;
        Assert.Equal(BattleOutcome.Won, first.Outcome);
        GameHost.BattleSetupCoordinator.CommitResolvedBattle(session, first);

        BattleFlowController second = Build(session);
        BattleFighter secondPartner = Assert.IsType<BattleFighter>(second.State.Players[0]);

        Assert.Equal(70, secondPartner.Hp);
        Assert.Equal(7, secondPartner.Mp);
        Assert.Equal([0, 6, 0], secondPartner.PersistentStatuses);
        Assert.Equal(0, session.Inventory[42]);
    }

    /// <summary>Builds a new flow through the host coordinator's engine-free production seam.</summary>
    /// <param name="session">Session reused by both battles.</param>
    /// <returns>Production-configured and started battle flow.</returns>
    private static BattleFlowController Build(PlayerSession session)
    {
        return GameHost.BattleSetupCoordinator.BuildForSession(
            new GameHost.BattleSetupCoordinator.BattleSessionBuildInput(
                Fighters(),
                Techniques(),
                Encounters(),
                1,
                session,
                Items(),
                Text(),
                new FixedRng()));
    }

    /// <summary>Builds the persistent three-partner session used by the production coordinator.</summary>
    /// <returns>Session with an item whose quantity must cross the battle boundary.</returns>
    private static PlayerSession CreateSession()
    {
        return new PlayerSession([Partner(1, 50, 10), Partner(2, 100, 10), Partner(3, 100, 10)], new Dictionary<int, int> { [42] = 1 });
    }

    /// <summary>Creates one session slot with supplied current battle resources.</summary>
    /// <param name="id">Partner Digimon id.</param>
    /// <param name="hp">Current HP.</param>
    /// <param name="mp">Current MP.</param>
    /// <returns>One persistent partner slot.</returns>
    private static PlayerSessionPartner Partner(int id, int hp, int mp) => new(new PartnerSeed { Id = id, Hp = hp, MaxHp = 100, Mp = mp, MaxMp = 10, Stats = Stats() }, [id], [1, 2], []);

    /// <summary>Builds the smallest complete fighter catalog accepted by the production setup path.</summary>
    /// <returns>Partner and enemy records for the consecutive battle.</returns>
    private static FighterData Fighters()
    {
        DigimonData[] partners = [Digimon(1), Digimon(2), Digimon(3)];
        return new FighterData(
            partners.ToDictionary(partner => partner.Id),
            partners,
            [new BattleTableEntry(100, 0, 0, 100, [1, 0, 0], [1, 1, 1, 1, 1], new int[12], 0, [new BattleAction(1, 0, 0)], new BattleAction(0, 0, 0))]);
    }

    /// <summary>Builds the one-enemy encounter used to resolve the first fight normally.</summary>
    /// <returns>Encounter catalog with one low-HP enemy.</returns>
    private static EncounterData Encounters() => new([], [new Dw3Production.Conversion.BattleEnemy(1, 100, 1, 1, 0, 16)], [new EncounterEntry(1, [1], [100], 0, 0, new int[12])], new Dictionary<string, EncounterStage>());

    /// <summary>Builds decoded techniques for the basic attack and MP-consuming technique command.</summary>
    /// <returns>Technique catalog.</returns>
    private static TechniqueData Techniques() => new(new Dictionary<int, Dw3Production.Conversion.TechData> { [1] = Tech(0, 1), [2] = Tech(3, 1000) });

    /// <summary>Builds the consumable healing item used through the flow's real item path.</summary>
    /// <returns>Battle item catalog.</returns>
    private static BattleItemsData Items() => new([new BattleItemData(42, 0, [], 0, 0, 20, 0, 0, 0, 0, "heal", "partner")]);

    /// <summary>Finishes a pending harmless enemy turn before the next player command.</summary>
    /// <param name="flow">Battle flow with a possible enemy turn.</param>
    private static void AdvanceEnemyTurn(BattleFlowController flow)
    {
        while (flow.Phase == BattleFlowPhase.EnemyTurn)
        {
            flow.EnemyAct(new FixedRng());
        }
    }

    /// <summary>Builds a valid stat source required by battle fighter creation.</summary>
    /// <returns>Player stats that permit a basic attack.</returns>
    private static BattleStats Stats()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = 10;
        stats.Stats[BattleStat.Defense] = 10;
        stats.Stats[BattleStat.Speed] = 100;
        return stats;
    }

    /// <summary>Builds a valid decoded partner record for the production fighter catalog.</summary>
    /// <param name="id">Partner Digimon id.</param>
    /// <returns>Partner data with the basic and technique-menu skills.</returns>
    private static DigimonData Digimon(int id) => new(id, [10, 10, 10, 10, 100], new int[7], [1, 2], 0, new int[5], [], [], 0, 0, 0, 100, 10, 0, 0, new int[5], new int[7], [], id, 0, false);

    /// <summary>Builds a decoded technique used by the normal production flow.</summary>
    /// <param name="mp">MP cost.</param>
    /// <param name="power">Attack power.</param>
    /// <returns>Technique record with deterministic hit accuracy.</returns>
    private static Dw3Production.Conversion.TechData Tech(int mp, int power) => new(0, mp, power, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    /// <summary>Builds an empty decoded text catalog sufficient for the state-only test.</summary>
    /// <returns>Empty source text catalog.</returns>
    private static BattleTextData Text() => new([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>());

    /// <summary>Provides deterministic favorable draws for the normal battle actions.</summary>
    private sealed class FixedRng : IBattleRng
    {
        /// <inheritdoc/>
        public int NextValue() => 0;
    }
}
