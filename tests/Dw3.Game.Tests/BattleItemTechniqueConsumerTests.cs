using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies the simulation contract for item-tech effects before session inventory owns their stock.</summary>
public sealed class BattleItemTechniqueConsumerTests
{
    /// <summary>Verifies that active-only item healing caps at the maximum and skips a knocked-out target.</summary>
    [Fact]
    public void Apply_ActiveHeal_CapsLivingActiveTarget()
    {
        BattleState state = CreateState();
        BattleFighter active = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        active.Hp = 95;

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xB8, 15, 20));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.Equal(100, active.Hp);
    }

    /// <summary>Verifies that side healing changes living existing partners but never revives a knocked-out or empty slot.</summary>
    [Fact]
    public void Apply_SideHeal_ChangesOnlyLivingExistingPartners()
    {
        BattleState state = CreateState();
        Assert.IsType<BattleFighter>(state.Players[0]).Hp = 80;
        Assert.IsType<BattleFighter>(state.Players[1]).Hp = 0;
        state.Players[2] = null;

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xBB, 15, 30));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.Equal(100, Assert.IsType<BattleFighter>(state.Players[0]).Hp);
        Assert.Equal(0, Assert.IsType<BattleFighter>(state.Players[1]).Hp);
        Assert.Null(state.Players[2]);
    }

    /// <summary>Verifies that each even cure clears only the selected active status.</summary>
    [Theory]
    [InlineData(0xBE, FighterFlags.Poisoned)]
    [InlineData(0xC0, FighterFlags.Paralyzed)]
    [InlineData(0xC2, FighterFlags.Confused)]
    [InlineData(0xC4, FighterFlags.Asleep)]
    public void Apply_ActiveCure_ClearsItsMappedStatus(int techniqueId, int flag)
    {
        BattleState state = CreateState();
        BattleFighter active = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        active.Flags |= flag;
        active.Paralysis = 5;
        active.Confusion = 6;
        active.Sleep = 7;

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, techniqueId, EffectForCure(techniqueId), 0));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.Equal(0, active.Flags & flag);
    }

    /// <summary>Verifies that an odd cure clears its mapped status from the whole living party.</summary>
    [Fact]
    public void Apply_SideCure_ClearsMappedStatusFromEveryLivingPartner()
    {
        BattleState state = CreateState();
        foreach (BattleFighter fighter in state.Players.OfType<BattleFighter>())
        {
            fighter.Flags |= FighterFlags.Confused;
            fighter.Confusion = 10;
        }

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xC3, 19, 0));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.All(state.Players.OfType<BattleFighter>(), fighter => Assert.Equal(0, fighter.Flags & FighterFlags.Confused));
    }

    /// <summary>Verifies that revive restores every knocked-out existing partner but does not modify living members.</summary>
    [Fact]
    public void Apply_Revive_RestoresKnockedOutExistingPartners()
    {
        BattleState state = CreateState();
        Assert.IsType<BattleFighter>(state.Players[0]).Hp = 60;
        Assert.IsType<BattleFighter>(state.Players[1]).Hp = 0;
        state.Players[2] = null;

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0x64, 21, 0));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.Equal(60, Assert.IsType<BattleFighter>(state.Players[0]).Hp);
        Assert.Equal(100, Assert.IsType<BattleFighter>(state.Players[1]).Hp);
        Assert.Null(state.Players[2]);
    }

    /// <summary>Verifies that full restore revives, refills HP and MP, and clears every status from existing partners.</summary>
    [Fact]
    public void Apply_FullRestore_RefreshesEveryExistingPartner()
    {
        BattleState state = CreateState();
        BattleFighter first = Assert.IsType<BattleFighter>(state.Players[0]);
        first.Hp = 20;
        first.Mp = 10;
        first.Flags = FighterFlags.Poisoned | FighterFlags.Asleep;
        Assert.IsType<BattleFighter>(state.Players[1]).Hp = 0;
        state.Players[2] = null;

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0x177, 36, 0));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.Equal(100, first.Hp);
        Assert.Equal(100, first.Mp);
        Assert.Equal(0, first.Flags & FighterFlags.Poisoned);
        Assert.Equal(0, first.Flags & FighterFlags.Asleep);
        Assert.Equal(100, Assert.IsType<BattleFighter>(state.Players[1]).Hp);
    }

    /// <summary>Verifies that auto recovery registers one deferred active-partner recovery without applying an invented immediate heal.</summary>
    [Fact]
    public void Apply_AutoRecovery_QueuesDeferredRecoveryWithoutImmediateHeal()
    {
        BattleState state = CreateState();
        BattleFighter active = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        active.Hp = 50;

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xBD, 16, 0));

        Assert.Equal(BattleItemTechniqueUseResult.Applied, result);
        Assert.Equal(50, active.Hp);
        Assert.Equal(new BattleAutoRecovery(BattleSide.Player, state.ActivePlayer, 0xBD), state.PendingAutoRecovery);
    }

    /// <summary>Verifies that named refusals leave all fighter resources and deferred recovery unchanged.</summary>
    [Fact]
    public void Apply_RefusedRequest_DoesNotMutateState()
    {
        BattleState state = CreateState();
        BattleFighter active = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        int hp = active.Hp;
        int mp = active.Mp;

        Assert.Equal(BattleItemTechniqueUseResult.InvalidTarget, BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xB8, 15, 10), activeSlot: 2));
        Assert.Equal(BattleItemTechniqueUseResult.UnknownEffect, BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xB8, 99, 10)));
        Assert.Equal(BattleItemTechniqueUseResult.OwnerNotPlayer, BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Enemy, 0xB8, 15, 10)));
        Assert.Equal(BattleItemTechniqueUseResult.NoBenefit, BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xB8, 15, 10)));
        Assert.Equal(hp, active.Hp);
        Assert.Equal(mp, active.Mp);
        Assert.Null(state.PendingAutoRecovery);
    }

    /// <summary>Verifies that an all-knocked-out party rejects item-tech use instead of fabricating a target.</summary>
    [Fact]
    public void Apply_AllPartnersKnockedOut_IsRefusedWithoutMutation()
    {
        BattleState state = CreateState();
        foreach (BattleFighter fighter in state.Players.OfType<BattleFighter>())
        {
            fighter.Hp = 0;
        }

        BattleItemTechniqueUseResult result = BattleItemTechniqueConsumer.Apply(state, new(BattleSide.Player, 0xB8, 15, 10));

        Assert.Equal(BattleItemTechniqueUseResult.InvalidTarget, result);
        Assert.All(state.Players.OfType<BattleFighter>(), fighter => Assert.Equal(0, fighter.Hp));
    }

    private static int EffectForCure(int techniqueId) => techniqueId switch
    {
        0xBE => 17,
        0xC0 => 18,
        0xC2 => 19,
        _ => 20,
    };

    private static BattleState CreateState()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(1);
        setup.Partners[1] = Partner(2);
        setup.Enemies[0] = new BattleEnemy { Fighter = 3, Hp = 100, Mp = 100, Strength = 16, BaseStats = new BattleStats() };
        return BattleState.InitFighters(setup, 1, new SequenceRng(127));
    }

    private static PartnerSeed Partner(int id) => new()
    {
        Id = id,
        Hp = 100,
        MaxHp = 100,
        Mp = 100,
        MaxMp = 100,
        Stats = new BattleStats(),
    };

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
