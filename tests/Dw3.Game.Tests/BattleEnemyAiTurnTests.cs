using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies enemy action-table decisions through the stateful turn boundary.</summary>
public sealed class BattleEnemyAiTurnTests
{
    /// <summary>Verifies that an unaffordable selected technique spends the turn without silently falling back.</summary>
    [Fact]
    public void RunEnemyTurn_UnaffordableSelectedTechnique_SpendsTurnWithoutFallback()
    {
        BattleState state = CreateState(new BattleTableAction(2, 0, 0), enemyMp: 5);
        state.Techs = new Dictionary<int, TechData> { [200] = new TechData { Mp = 6, Accuracy = 128, Power = 10 } };

        EnemyDecision decision = state.RunEnemyTurn(new ZeroRng());

        Assert.Equal(EnemyActionKind.Technique1, decision.Kind);
        Assert.Equal(5, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Mp);
        Assert.Equal(1, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Turns);
    }

    /// <summary>Verifies that a selected technique absent from the loaded catalogue fails with its action and technique identifiers named.</summary>
    [Fact]
    public void RunEnemyTurn_UnknownSelectedTechnique_FailsWithNamedCatalogError()
    {
        BattleState state = CreateState(new BattleTableAction(2, 0, 0));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => state.RunEnemyTurn(new ZeroRng()));

        Assert.Equal("enemy-ai-technique-not-found action=Technique1 technique=200", error.Message);
    }

    /// <summary>Verifies that a switch action with no qualifying reserve keeps the active enemy and spends its turn.</summary>
    [Fact]
    public void RunEnemyTurn_InvalidSwitchTarget_KeepsActiveEnemyAndSpendsTurn()
    {
        BattleState state = CreateState(new BattleTableAction(6, 0, 0));

        EnemyDecision decision = state.RunEnemyTurn(new ZeroRng());

        Assert.Equal(EnemyActionKind.SwitchTo1, decision.Kind);
        Assert.Equal(0, state.ActiveEnemy);
        Assert.Equal(1, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Turns);
    }

    /// <summary>Verifies that a qualifying replacement action brings the requested living reserve in.</summary>
    [Fact]
    public void RunEnemyTurn_QualifiedReplacement_ActivatesRequestedReserve()
    {
        BattleState state = CreateState(new BattleTableAction(6, 0, 0));
        state.Enemies[1] = new BattleFighter { Id = 3, Hp = 100 };

        EnemyDecision decision = state.RunEnemyTurn(new ZeroRng());

        Assert.Equal(EnemyActionKind.SwitchTo1, decision.Kind);
        Assert.Equal(1, state.ActiveEnemy);
        Assert.Equal(3, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Id);
    }

    /// <summary>Verifies that an enemy run-away action can end the battle as fled.</summary>
    [Fact]
    public void RunEnemyTurn_RunAwayActionSucceeds_MarksBattleFled()
    {
        BattleState state = CreateState(new BattleTableAction(4, 0, 0));

        EnemyDecision decision = state.RunEnemyTurn(new ZeroRng());

        Assert.Equal(EnemyActionKind.RunAway, decision.Kind);
        Assert.Equal(BattleOutcome.Fled, state.Outcome);
    }

    /// <summary>Verifies that a player sleep condition wins priority over the unconditional fallback.</summary>
    [Fact]
    public void DecideEnemyTurn_PlayerAsleepCondition_PicksItsEarlierAction()
    {
        BattleState state = CreateState(
            new BattleTableAction(4, 9, 0),
            new BattleTableAction(1, 0, 0));
        Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Flags |= FighterFlags.Asleep;

        EnemyDecision decision = state.DecideEnemyAction(new ZeroRng());

        Assert.Equal(EnemyActionKind.RunAway, decision.Kind);
    }

    /// <summary>Creates a deterministic two-fighter battle with an explicit action-table prefix.</summary>
    /// <param name="first">First action-table row.</param>
    /// <param name="fallback">Fallback action-table row.</param>
    /// <param name="enemyMp">Starting MP for the enemy.</param>
    /// <returns>A battle state with the enemy active in slot zero.</returns>
    private static BattleState CreateState(BattleTableAction first, BattleTableAction? fallback = null, int enemyMp = 100)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(1);
        setup.Enemies[0] = Enemy(2, enemyMp, [first, fallback ?? new BattleTableAction(1, 0, 0)]);
        return BattleState.InitFighters(setup, digimon: 1, new ZeroRng());
    }

    /// <summary>Creates a player seed with neutral stats.</summary>
    /// <param name="id">Player Digimon identifier.</param>
    /// <returns>A living player seed.</returns>
    private static PartnerSeed Partner(int id) => new()
    {
        Id = id,
        Hp = 100,
        MaxHp = 100,
        Mp = 100,
        MaxMp = 100,
        Stats = Stats(),
    };

    /// <summary>Creates an enemy entry with the supplied AI rows.</summary>
    /// <param name="id">Enemy fighter identifier.</param>
    /// <param name="mp">Starting MP.</param>
    /// <param name="actions">Action-table rows.</param>
    /// <returns>A living enemy entry.</returns>
    private static BattleEnemy Enemy(int id, int mp, BattleTableAction[] actions) => new()
    {
        Fighter = id,
        Hp = 100,
        Mp = mp,
        Strength = 16,
        BaseStats = Stats(),
        Techs = [100, 200, 0],
        Actions = actions,
    };

    /// <summary>Creates neutral resolved stats with a nonzero speed.</summary>
    /// <returns>Battle stats used by both sides.</returns>
    private static BattleStats Stats()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Speed] = 100;
        return stats;
    }

    /// <summary>Supplies deterministic successful low random values.</summary>
    private sealed class ZeroRng : IBattleRng
    {
        /// <summary>Returns the fixed low random value.</summary>
        /// <returns>Zero.</returns>
        public int NextValue() => 0;
    }
}
