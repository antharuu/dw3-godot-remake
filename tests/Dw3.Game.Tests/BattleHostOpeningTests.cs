using System.Reflection;
using Dw3Production;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleHostOpeningTests
{
    [Theory]
    [InlineData(0, BattleSide.Enemy)]
    [InlineData(127, BattleSide.Player)]
    public void CompiledProductionSetupPassesItsHostStreamToTheOpening(int roll, BattleSide expected)
    {
        Type coordinator = typeof(GameHost).GetNestedType("BattleSetupCoordinator", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing production setup coordinator.");
        FieldInfo host = coordinator.GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing setup host.");
        FieldInfo stream = typeof(GameHost).GetField("_battleRng", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing production stream.");
        MethodInfo initialize = coordinator.GetMethod("CreateOpeningState", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing production opening seam.");
        byte[] body = coordinator.GetMethod("Build")?.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException("Missing production setup body.");

        // Verify the actual compiled caller, not a duplicate test-only setup path. The final
        // argument must come from this coordinator's host immediately before the opening seam.
        byte[] injection = [0x02, 0x7b, .. BitConverter.GetBytes(host.MetadataToken),
            0x7b, .. BitConverter.GetBytes(stream.MetadataToken), 0x28, .. BitConverter.GetBytes(initialize.MetadataToken)];
        Assert.True(body.AsSpan().IndexOf(injection) >= 0, "Production setup must pass _host._battleRng to CreateOpeningState.");

        var rng = new OpeningRng(roll);
        var state = Assert.IsType<BattleState>(initialize.Invoke(null, [Setup(), 1, rng]));

        Assert.Equal(1, rng.DrawCount);
        Assert.Equal(expected, state.NextActor);
    }

    private static BattleSetup Setup()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Stats = stats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 100, Strength = 16, BaseStats = stats };
        return setup;
    }

    private sealed class OpeningRng(int roll) : IBattleRng
    {
        public int DrawCount { get; private set; }

        public int NextValue()
        {
            DrawCount++;
            return roll;
        }
    }
}
