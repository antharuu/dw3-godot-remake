using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that the pair-menu consumer resolves its configured relation through the battle flow.</summary>
public sealed class BattlePairMenuIntegrationTests
{
    /// <summary>Choosing a configured pair through the flow debits both partners, damages the target and activates the incoming partner.</summary>
    [Fact]
    public void ChoosePairTechnique_ConfiguredRelation_ResolvesThroughFlow()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Seed(386, 500, 500);
        setup.Partners[1] = Seed(374, 500, 500);
        setup.Enemies[0] = new BattleEnemy { Fighter = 99, Hp = 99999, Mp = 0, Strength = 16, BaseStats = Stats(1) };
        var flow = new BattleFlowController
        {
            PairTechniquesByForm = new Dictionary<int, PairTechniqueDefinition> { [386] = new(432, 74) },
            NameIds = new Dictionary<int, int> { [374] = 74 },
            Techs = new Dictionary<int, TechData> { [432] = new TechData { Accuracy = 128, Power = 50, Mp = 80 } },
        };
        flow.Start(BattleState.InitFighters(setup, 386, new ZeroRng()));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        int enemyHp = enemy.Hp;

        bool spent = flow.ChoosePairTechnique(1, new ZeroRng());

        Assert.True(spent);
        Assert.Equal(420, flow.State.Players[0]?.Mp);
        Assert.Equal(420, flow.State.Players[1]?.Mp);
        Assert.True(enemy.Hp < enemyHp);
        Assert.Equal(1, flow.State.ActivePlayer);
        Assert.True(flow.State.Fought[1]);
    }

    /// <summary>Builds a living partner seed with deterministic resolved statistics.</summary>
    private static PartnerSeed Seed(int id, int hp, int mp) => new()
    {
        Id = id,
        Hp = hp,
        MaxHp = hp,
        Mp = mp,
        MaxMp = mp,
        Stats = Stats(100),
    };

    /// <summary>Builds the minimum combat statistics required by the deterministic fixture.</summary>
    private static BattleStats Stats(int value)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = value;
        stats.Stats[BattleStat.Defense] = value;
        stats.Stats[BattleStat.Speed] = value;
        return stats;
    }

    /// <summary>Provides deterministic zero rolls for the fixture.</summary>
    private sealed class ZeroRng : IBattleRng
    {
        /// <inheritdoc/>
        public int NextValue() => 0;
    }
}
