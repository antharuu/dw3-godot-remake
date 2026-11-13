using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleHitResolverTests
{
    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void PhysicalPlayerHitUsesTechniqueAccuracyAtTheLowSevenBitBoundary(int roll, bool expected)
    {
        var tech = new TechData { Accuracy = 64 };
        var attacker = Fighter(level: 10, speed: 80);
        var target = Fighter(level: 10, speed: 80);

        bool hit = BattleHitRules.RollPhysical(tech, attacker.Stats, target.Stats, enemyAttacker: false, roll);

        Assert.Equal(expected, hit);
    }

    [Fact]
    public void PhysicalPlayerHitAppliesAccuracyBonusButEnemyUsesTargetEvasion()
    {
        var tech = new TechData { Accuracy = 64 };
        var player = Fighter(level: 10, speed: 80);
        player.Stats.Accuracy = 16;
        var enemy = Fighter(level: 10, speed: 80);
        enemy.Stats.Evasion = 16;

        bool playerHit = BattleHitRules.RollPhysical(tech, player.Stats, enemy.Stats, enemyAttacker: false, rngValue: 64);
        bool enemyHit = BattleHitRules.RollPhysical(tech, player.Stats, enemy.Stats, enemyAttacker: true, rngValue: 63);

        Assert.True(playerHit);
        Assert.False(enemyHit);
    }

    [Fact]
    public void EnemyPhysicalHitClampsItsChanceToOneQuarter()
    {
        var tech = new TechData { Accuracy = 1 };
        var attacker = Fighter(level: 1, speed: 1);
        var target = Fighter(level: 99, speed: 999);
        target.Stats.Evasion = 999;

        bool hitsAtFloor = BattleHitRules.RollPhysical(tech, attacker.Stats, target.Stats, enemyAttacker: true, rngValue: 31);
        bool missesAfterFloor = BattleHitRules.RollPhysical(tech, attacker.Stats, target.Stats, enemyAttacker: true, rngValue: 32);

        Assert.True(hitsAtFloor);
        Assert.False(missesAfterFloor);
    }

    [Fact]
    public void MagicHitUsesWisdomDifferenceInsteadOfPhysicalEquipmentBonuses()
    {
        var tech = new TechData { Accuracy = 64, Icon = BattleConst.TechMagic };
        var attacker = Fighter(level: 10, wisdom: 96, speed: 0);
        attacker.Stats.Accuracy = 120;
        var target = Fighter(level: 10, wisdom: 32, speed: 999);
        target.Stats.Evasion = 120;

        bool hit = BattleHitRules.RollMagic(tech, attacker.Stats, target.Stats, rngValue: 67);

        Assert.True(hit);
    }

    [Fact]
    public void PlayerMissConsumesHitAndCriticalChecksButDoesNotDamageOrCounter()
    {
        var state = CreateBattle();
        state.AllowCritical = true;
        var target = state.ActiveEnemyFighter!;
        var attacker = state.ActivePlayerFighter!;
        var rng = new RecordingRng(127, 127, 42);
        var tech = new TechData { Power = 50, Accuracy = 1 };

        bool acted = state.PlayerAttack(tech, rng);

        Assert.True(acted);
        Assert.Equal(500, target.Hp);
        Assert.Equal(500, attacker.Hp);
        Assert.Equal([127, 127], rng.Draws);
    }

    [Fact]
    public void PhysicalHitRollPrecedesCriticalRollAndCriticalStrength()
    {
        var rng = new RecordingRng(0, 0, 31);
        var resolver = new BattleHitResolver(ElementBoost.None, allowCritical: true, rng);
        var attacker = Fighter(level: 10, attack: 20, defense: 20, speed: 20);
        var target = Fighter(level: 10, attack: 20, defense: 20, speed: 20);
        var tech = new TechData { Power = 10, Accuracy = 128 };

        BattleHitResult result = resolver.Basic(tech, attacker, target, enemyAttacker: false);

        Assert.True(result.Landed);
        Assert.True(result.Damage > 10);
        Assert.Equal([0, 0, 31], rng.Draws);
    }

    [Fact]
    public void MagicHitRollPrecedesMagicCriticalRollAndCriticalStrength()
    {
        var rng = new RecordingRng(0, 0, 64);
        var resolver = new BattleHitResolver(ElementBoost.None, allowCritical: true, rng);
        var attacker = Fighter(level: 10, spirit: 20, wisdom: 20, speed: 20);
        var target = Fighter(level: 10, spirit: 20, wisdom: 20, speed: 20);
        target.Stats.Resist[0] = 100;
        var tech = new TechData { Accuracy = 128, Element = BattleConst.ElementFirst, ElementPower = 64, Icon = BattleConst.TechMagic, Power = 10 };

        BattleHitResult result = resolver.Technique(tech, attacker, target, enemyAttacker: false);

        Assert.True(result.Landed);
        Assert.True(result.Damage > tech.Power);
        Assert.Equal([0, 0, 64], rng.Draws);
    }

    [Fact]
    public void MagicEnemyMissConsumesHitAndCriticalChecksButDoesNotDamage()
    {
        var state = CreateBattle();
        state.AllowCritical = true;
        var target = state.ActivePlayerFighter!;
        target.Stats.Resist[0] = 100;
        var rng = new RecordingRng(127, 127, 42);
        var tech = new TechData { Accuracy = 1, Icon = BattleConst.TechMagic, Element = BattleConst.ElementFirst, ElementPower = 1, Power = 10 };

        bool acted = state.EnemyTechnique(tech, rng);

        Assert.True(acted);
        Assert.Equal(500, target.Hp);
        Assert.Equal([127, 127], rng.Draws);
    }

    [Fact]
    public void EnemyCounterMissConsumesOnlyItsHitCheckAndDoesNotDamage()
    {
        var state = CreateBattle();
        state.AllowCritical = true;
        var target = state.ActivePlayerFighter!;
        var rng = new RecordingRng(127, 0, 31);
        var tech = new TechData { Accuracy = 1, EffectPower = 64 };

        state.EnemyCounter(tech, receivedDamage: 50, rng);

        Assert.Equal(500, target.Hp);
        Assert.Equal([127], rng.Draws);
    }

    [Theory]
    [InlineData(false, 0, -1, 0)]
    [InlineData(true, 127, -1, 1)]
    [InlineData(true, 0, 64, 2)]
    public void DirectItemMagic_UsesOnlyTheQualifiedCriticalPolicy(bool allowCritical, int criticalRoll, int expectedStrength, int expectedDraws)
    {
        var rng = new RecordingRng(criticalRoll, 64);
        var resolver = new BattleHitResolver(ElementBoost.None, allowCritical, rng);
        var attacker = Fighter(level: 10, spirit: 50);
        var target = Fighter(level: 10, spirit: 50);
        target.Stats.Resist[0] = 100;
        var tech = new TechData { Power = 20, Icon = BattleConst.TechMagic, Element = BattleConst.ElementFirst, ElementPower = 1 };

        int damage = resolver.DirectItemMagic(tech, attacker, target);

        int expected = DamageCalculator.ComputeMagicDamage(tech, attacker.Stats, target.Stats, enemyAttacker: false, attacker.Strength, ElementBoost.None, expectedStrength);
        Assert.Equal(expected, damage);
        Assert.Equal(expectedDraws, rng.Draws.Count);
    }

    private static BattleState CreateBattle()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 500, MaxHp = 500, Mp = 100, MaxMp = 100, Stats = Fighter(level: 10, attack: 50, defense: 50, spirit: 50, wisdom: 50, speed: 50).Stats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 500, Mp = 100, Strength = 16, BaseStats = Fighter(level: 10, attack: 50, defense: 50, spirit: 50, wisdom: 50, speed: 50).Stats };
        return BattleState.InitFighters(setup, digimon: 1);
    }

    private static BattleFighter Fighter(int level, int attack = 0, int defense = 0, int spirit = 0, int wisdom = 0, int speed = 0)
    {
        var stats = new BattleStats { Level = level };
        stats.Stats[BattleStat.Attack] = attack;
        stats.Stats[BattleStat.Defense] = defense;
        stats.Stats[BattleStat.Spirit] = spirit;
        stats.Stats[BattleStat.Wisdom] = wisdom;
        stats.Stats[BattleStat.Speed] = speed;
        var seed = new PartnerSeed { Hp = 500, MaxHp = 500, Stats = stats };
        return BattleFighter.FromPartner(seed, id: 0);
    }

    private sealed class RecordingRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public List<int> Draws { get; } = [];

        public int NextValue()
        {
            int value = _values.Dequeue();
            Draws.Add(value);
            return value;
        }
    }
}
