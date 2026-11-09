using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Verifies that the production setup and resolution seams carry one session across battles.</summary>
public sealed class BattleSessionFlowTests
{
    /// <summary>Uses the production session path to reject unavailable forms and persist a real form change.</summary>
    [Fact]
    public void ProductionDigivolution_UsesKnownFormsRefreshesStatsAndPersistsTheSelectedForm()
    {
        PlayerSession session = CreateDigivolutionSession();
        DigivolutionData digivolutions = Digivolutions();
        BattleFlowController first = Build(session, digivolutions);

        Assert.Equal([2], first.PlayerDigivolutions);
        Assert.Equal([2], first.PlayerTechniques.Select(technique => technique.Id));
        Assert.False(first.Choose(BattleCommand.Digivolve, 3, new FixedRng()));
        Assert.False(first.Choose(BattleCommand.Digivolve, 1, new FixedRng()));
        Assert.True(first.Choose(BattleCommand.Digivolve, 2, new FixedRng()));
        Assert.Equal(2, first.State.ActivePlayerFighter!.Id);
        Assert.Equal(10, first.State.ActivePlayerFighter.Mp);
        Assert.Equal(2, first.NameIds[2]);
        Assert.Equal(25, first.State.ActivePlayerFighter.Stats.Stats[BattleStat.Attack]);
        Assert.Equal([1], first.PlayerTechniques.Select(technique => technique.Id));

        GameHost.BattleSetupCoordinator.CommitResolvedBattle(session, first);
        BattleFlowController second = Build(session, digivolutions);

        Assert.Equal(2, second.State.ActivePlayerFighter!.Id);
        Assert.Equal(25, second.State.ActivePlayerFighter.Stats.Stats[BattleStat.Attack]);
        Assert.Empty(second.PlayerDigivolutions);
        Assert.False(second.Choose(BattleCommand.Digivolve, 1, new FixedRng()));
    }

    /// <summary>Rejects an owned form from the production menu when its extracted stat gate is not met.</summary>
    [Fact]
    public void ProductionDigivolution_RejectsOwnedFormWhenExtractedConditionFails()
    {
        PlayerSession session = CreateDigivolutionSession();
        var digivolutions = new DigivolutionData(
            [new PartnerDigivolution(0, 1, [new DigivolutionEntry(1, 2, [], Stat: 1, Value: 26)])],
            new DigivolutionBlast([]));

        BattleFlowController flow = Build(session, digivolutions);

        Assert.Empty(flow.PlayerDigivolutions);
        Assert.False(flow.Choose(BattleCommand.Digivolve, 2, new FixedRng()));
        Assert.Equal(1, flow.State.ActivePlayerFighter!.Id);
    }

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
    /// <param name="digivolutions">Optional extracted evolution requirements.</param>
    /// <returns>Production-configured and started battle flow.</returns>
    private static BattleFlowController Build(PlayerSession session, DigivolutionData? digivolutions = null)
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
                new FixedRng(),
                digivolutions));
    }

    /// <summary>Builds the persistent three-partner session used by the production coordinator.</summary>
    /// <returns>Session with an item whose quantity must cross the battle boundary.</returns>
    private static PlayerSession CreateSession()
    {
        return new PlayerSession([Partner(1, 50, 10), Partner(2, 100, 10), Partner(3, 100, 10)], new Dictionary<int, int> { [42] = 1 });
    }

    /// <summary>Builds a session where only form two is owned besides the current base form.</summary>
    /// <returns>Session with a source-backed alternate stat snapshot.</returns>
    private static PlayerSession CreateDigivolutionSession()
    {
        BattleStats baseStats = Stats();
        baseStats.Level = 1;
        var alternate = Stats();
        alternate.Stats[BattleStat.Attack] = 25;
        var first = new PartnerSeed { Id = 1, Hp = 50, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = baseStats, FormStats = new Dictionary<int, BattleStats> { [2] = alternate } };
        return new PlayerSession(
            [new PlayerSessionPartner(first, [1, 2], [1, 2], []), Partner(2, 100, 10), Partner(3, 100, 10)],
            new Dictionary<int, int> { [42] = 1 });
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
        DigimonData[] partners = [Digimon(1), Digimon(2, [2, 1]), Digimon(3)];
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

    /// <summary>Builds the extracted ownership and level gates for the first partner's normal form menu.</summary>
    /// <returns>Digivolution data with one eligible and one unknown target.</returns>
    private static DigivolutionData Digivolutions() => new(
        [new PartnerDigivolution(
            Partner: 0,
            BaseId: 1,
            Evolutions:
            [
                new DigivolutionEntry(0, 2, [new EvolutionNeed(0, 1, 1)], Stat: 7, Value: 1),
                new DigivolutionEntry(1, 3, [], Stat: 7, Value: 1),
            ])],
        new DigivolutionBlast([]));

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
    /// <param name="id">Partner form id.</param>
    /// <param name="skills">Optional source skill slots for the form.</param>
    /// <returns>Partner data with the basic and technique-menu skills.</returns>
    private static DigimonData Digimon(int id, IReadOnlyList<int>? skills = null) => new(id, [10, 10, 10, 10, 100], new int[7], skills ?? [1, 2], 0, new int[5], [], [], 0, 0, 0, 100, 10, 0, 0, new int[5], new int[7], [], id, 0, false);

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
