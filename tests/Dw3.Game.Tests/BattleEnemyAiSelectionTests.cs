using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies source-qualified selection and rejection paths for enemy battle-table actions.</summary>
public sealed class BattleEnemyAiSelectionTests
{
    /// <summary>Verifies that a fixed enemy switch rejects a reserve with the active fighter's identifier.</summary>
    [Fact]
    public void ResolveSwitch_FixedDuplicateFighterId_RejectsTheReserve()
    {
        var active = new BattleFighter { Id = 11, Hp = 100 };
        var duplicateReserve = new BattleFighter { Id = 11, Hp = 100 };

        int slot = BattleEnemyAi.ResolveSwitch(
            new EnemyDecision(EnemyActionKind.SwitchTo1, 0, 1),
            [active, duplicateReserve],
            activeEnemy: 0,
            new ZeroRng());

        Assert.Equal(-1, slot);
    }

    /// <summary>Verifies that an unrecognised catalogue target stops selection with its target value named.</summary>
    [Fact]
    public void PickAction_UnsupportedTarget_FailsWithNamedCatalogError()
    {
        var enemy = new BattleFighter
        {
            Actions = [new BattleTableAction(9, 0, 0)],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BattleEnemyAi.PickAction(enemy, new EnemyAiContext { Enemy = enemy }, new ZeroRng()));

        Assert.Equal("enemy-ai-unsupported-action target=9", error.Message);
    }

    /// <summary>Verifies that an unrecognised condition cannot silently become an eligible action.</summary>
    [Fact]
    public void TestEnemyCondition_UnsupportedCondition_FailsWithNamedCatalogError()
    {
        var enemy = new BattleFighter();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BattleEnemyAi.TestEnemyCondition(19, 0, new EnemyAiContext { Enemy = enemy }, new ZeroRng()));

        Assert.Equal("enemy-ai-unsupported-condition condition=19", error.Message);
    }

    /// <summary>Verifies that a fixed switch target outside the roster fails as a named action-table error.</summary>
    [Fact]
    public void ResolveSwitch_FixedSlotOutsideRoster_FailsWithNamedCatalogError()
    {
        var active = new BattleFighter { Id = 11, Hp = 100 };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => BattleEnemyAi.ResolveSwitch(
                new EnemyDecision(EnemyActionKind.SwitchTo2, 0, 2),
                [active],
                activeEnemy: 0,
                new ZeroRng()));

        Assert.Equal("enemy-ai-invalid-switch-slot slot=2 roster-count=1", error.Message);
    }

    /// <summary>Supplies a deterministic random stream for paths that must not consume it.</summary>
    private sealed class ZeroRng : IBattleRng
    {
        /// <summary>Returns the fixed zero value.</summary>
        /// <returns>Zero.</returns>
        public int NextValue() => 0;
    }
}
