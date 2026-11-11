using Dw3Production.Simulation;
using Xunit;
using Conversion = Dw3Production.Conversion;

namespace Dw3.Game.Tests;

/// <summary>Verifies the source-qualified pair-technique conditions and resolution (B9-042).</summary>
public sealed class BattlePairTechniqueTests
{
    private const int ActiveFormId = 386;
    private const int IncomingFormId = 374;
    private const int IncomingNameId = 74;
    private const int PairTechniqueId = 432;
    private const int PairCost = 80;

    /// <summary>Oracle B9-076: a valid pair technique debits 80 MP from both fighters, resolves damage and brings in the selected partner.</summary>
    [Fact]
    public void PairTechnique_ValidPair_DebitsBothFightersResolvesDamageAndSwitches()
    {
        BattleState state = CreateState();
        BattleFighter outgoing = Assert.IsType<BattleFighter>(state.Players[0]);
        BattleFighter incoming = Assert.IsType<BattleFighter>(state.Players[1]);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        int enemyHpBefore = enemy.Hp;

        bool spent = UsePair(state);

        Assert.True(spent);
        Assert.Equal(9999 - PairCost, outgoing.Mp);
        Assert.Equal(9999 - PairCost, incoming.Mp);
        Assert.True(enemy.Hp < enemyHpBefore);
        Assert.Equal(1, state.ActivePlayer);
        Assert.Equal(IncomingFormId, state.ActivePlayerFighter?.Id);
        Assert.False(state.Fought[0]);
        Assert.True(state.Fought[1]);
    }

    /// <summary>When a pair technique ends the fight, the source marks only the incoming partner and does not perform the switch.</summary>
    [Fact]
    public void PairTechnique_FinalEnemyDefeated_MarksIncomingWithoutSwitching()
    {
        BattleState state = CreateState(enemyHp: 1);

        bool spent = UsePair(state);

        Assert.True(spent);
        Assert.Equal(0, state.ActivePlayer);
        Assert.False(state.Fought[0]);
        Assert.True(state.Fought[1]);
    }

    /// <summary>A knocked-out matching partner cannot provide a pair technique and leaves all state untouched.</summary>
    [Fact]
    public void PairTechnique_PartnerUnavailable_IsRefusedWithoutSpendingResources()
    {
        BattleState state = CreateState(incomingHp: 0);
        BattleFighter outgoing = Assert.IsType<BattleFighter>(state.Players[0]);
        BattleFighter incoming = Assert.IsType<BattleFighter>(state.Players[1]);

        bool spent = UsePair(state);

        Assert.False(spent);
        Assert.Equal(9999, outgoing.Mp);
        Assert.Equal(9999, incoming.Mp);
        Assert.Equal(0, state.ActivePlayer);
        Assert.False(state.Fought[0]);
        Assert.False(state.Fought[1]);
    }

    /// <summary>Either fighter below the shared technique cost refuses the pair technique without partially debiting the other fighter.</summary>
    [Theory]
    [InlineData(79, 9999)]
    [InlineData(9999, 79)]
    public void PairTechnique_InsufficientResources_IsRefusedWithoutPartialDebit(int activeMp, int incomingMp)
    {
        BattleState state = CreateState(activeMp, incomingMp);

        bool spent = UsePair(state);

        Assert.False(spent);
        Assert.Equal(activeMp, state.Players[0]?.Mp);
        Assert.Equal(incomingMp, state.Players[1]?.Mp);
        Assert.Equal(0, state.ActivePlayer);
    }

    /// <summary>A matching technique cannot be invoked with an incoming form whose source name id does not match the relation.</summary>
    [Fact]
    public void PairTechnique_WrongPartnerName_IsRefusedWithoutSpendingResources()
    {
        BattleState state = CreateState();
        TechData technique = Assert.IsType<TechData>(state.Techs[PairTechniqueId]);

        bool spent = state.PlayerPairTechnique(1, new PairTechniqueDefinition(PairTechniqueId, IncomingNameId), IncomingNameId + 1, technique, new FixedRng());

        Assert.False(spent);
        Assert.Equal(9999, state.Players[0]?.Mp);
        Assert.Equal(9999, state.Players[1]?.Mp);
        Assert.Equal(0, state.ActivePlayer);
    }

    /// <summary>Pair definition creation reads the decoded active-form pair fields rather than inferring a relation from ids.</summary>
    [Fact]
    public void PairTechniqueDefinition_SourcePairFields_CreateNameMatchedDefinition()
    {
        Conversion.DigimonData active = CreateDigimon(ActiveFormId, PairTechniqueId, IncomingNameId, 21);
        Conversion.DigimonData incoming = CreateDigimon(IncomingFormId, 0, 0, IncomingNameId);
        var fighters = new Conversion.FighterData(
            new Dictionary<int, Conversion.DigimonData> { [active.Id] = active, [incoming.Id] = incoming },
            [active, incoming],
            []);

        PairTechniqueDefinition? definition = PairTechniqueCatalog.ForActiveForm(fighters, ActiveFormId);

        Assert.Equal(new PairTechniqueDefinition(PairTechniqueId, IncomingNameId), definition);
        Assert.Null(PairTechniqueCatalog.ForActiveForm(fighters, IncomingFormId));
    }

    private static Conversion.DigimonData CreateDigimon(int id, int pairTechnique, int pairPartner, int nameId) =>
        new(id, [], [], [1], pairTechnique, [], [], [], 0, pairPartner, 0, 0, 0, 0, 0, [], [], [], nameId, 0, false);

    private static bool UsePair(BattleState state)
    {
        TechData technique = Assert.IsType<TechData>(state.Techs[PairTechniqueId]);
        return state.PlayerPairTechnique(1, new PairTechniqueDefinition(PairTechniqueId, IncomingNameId), IncomingNameId, technique, new FixedRng());
    }

    private static BattleState CreateState(int activeMp = 9999, int incomingMp = 9999, int incomingHp = 300, int enemyHp = 10000)
    {
        BattleStats playerStats = CreateStats(attack: 100, defense: 100);
        BattleStats enemyStats = CreateStats(attack: 1, defense: 1);
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = ActiveFormId, Hp = 300, MaxHp = 300, Mp = activeMp, MaxMp = 9999, Stats = playerStats };
        setup.Partners[1] = new PartnerSeed { Id = IncomingFormId, Hp = incomingHp, MaxHp = 300, Mp = incomingMp, MaxMp = 9999, Stats = playerStats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 99, Hp = enemyHp, Mp = 0, Strength = 16, BaseStats = enemyStats };
        BattleState state = BattleState.InitFighters(setup, ActiveFormId, new FixedRng());
        state.Techs = new Dictionary<int, TechData>
        {
            [PairTechniqueId] = new TechData { Accuracy = 128, Power = 50, HitCount = 1, Mp = PairCost },
        };
        return state;
    }

    private static BattleStats CreateStats(int attack, int defense)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = attack;
        stats.Stats[BattleStat.Defense] = defense;
        stats.Stats[BattleStat.Speed] = 100;
        return stats;
    }

    private sealed class FixedRng : IBattleRng
    {
        /// <inheritdoc/>
        public int NextValue() => 0;
    }
}
