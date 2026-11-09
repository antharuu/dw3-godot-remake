using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Verifies the session-owned sources used to create consecutive battles.</summary>
public sealed class PlayerSessionTests
{
    /// <summary>Persists HP, MP and status changes from one battle into the next setup.</summary>
    [Fact]
    public void CommitBattle_PreservesResourcesAndPersistentStatusesForTheNextBattle()
    {
        var session = new PlayerSession(
            [
                new PlayerSessionPartner(
                    new PartnerSeed
                    {
                        Id = 11,
                        Hp = 90,
                        MaxHp = 100,
                        Mp = 30,
                        MaxMp = 40,
                        Stats = new BattleStats(),
                    },
                    knownForms: [11, 12],
                    equippedForms: [12],
                    knownTechniques: [101, 102],
                    equipmentIds: [7]),
            ],
            new Dictionary<int, int> { [42] = 3 });
        PartnerSeed?[] firstBattle = session.BuildParty();
        BattleFighter fighter = BattleFighter.FromPartner(firstBattle[0]!, 11);
        fighter.Hp = 21;
        fighter.Mp = 9;
        fighter.PersistentStatuses[1] = 6;

        session.CommitBattle([fighter, null, null]);
        PartnerSeed?[] secondBattle = session.BuildParty();

        PartnerSeed secondPartner = Assert.IsType<PartnerSeed>(secondBattle[0]);
        Assert.Equal(21, secondPartner.Hp);
        Assert.Equal(9, secondPartner.Mp);
        Assert.Equal([0, 6, 0], secondPartner.PersistentStatuses);
        Assert.Equal([11, 12], session.Partners[0]!.KnownForms);
        Assert.Equal([12], session.Partners[0]!.EquippedForms);
        Assert.Equal([101, 102], session.Partners[0]!.KnownTechniques);
        Assert.Equal([7], session.Partners[0]!.EquipmentIds);
        Assert.Equal(3, session.Inventory[42]);
    }

    /// <summary>Provides an isolated enemy-free party source for the battle-state regression.</summary>
    [Fact]
    public void BuildParty_ReturnsIndependentSlotsWithoutResettingTheSession()
    {
        var session = new PlayerSession(
            [new PlayerSessionPartner(new PartnerSeed { Id = 11, Hp = 10, MaxHp = 10, Mp = 5, MaxMp = 5, Stats = new BattleStats() }, [11], [], [], [])],
            new Dictionary<int, int>());

        PartnerSeed?[] first = session.BuildParty();
        PartnerSeed?[] second = session.BuildParty();

        Assert.Same(first[0], second[0]);
        Assert.Equal(10, second[0]!.Hp);
    }
}
