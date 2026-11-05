using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleTurnSchedulerTests
{
    [Fact]
    public void InitializeOpeningUsesTheInjectedRollForEqualSpeeds()
    {
        var scheduler = new BattleTurnScheduler();

        scheduler.InitializeOpening(playerSpeed: 100, enemySpeed: 100, ambushChance: 0, new SequenceRng(7));

        var next = scheduler.PopNext();

        Assert.Equal(BattleScheduledEventKind.EnemyTurn, next.Kind);
        Assert.Equal(BattleSide.Enemy, next.Side);
        Assert.Equal(0, scheduler.Elapsed);
    }

    [Fact]
    public void PopNextUsesTheLaterTurnSlotForAnEqualDueTime()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 500);
        scheduler.EnqueueTurn(BattleSide.Enemy, delay: 500);

        var next = scheduler.PopNext();

        Assert.Equal(BattleSide.Enemy, next.Side);
        Assert.Equal(500, scheduler.Elapsed);
    }

    [Fact]
    public void PriorityInterruptionPrecedesExistingTurnsWithoutDuplicatingThem()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 10);
        scheduler.EnqueuePriority(BattleScheduledEventKind.Interrupt, BattleSide.Enemy, requestedDelay: 1);

        var interrupt = scheduler.PopNext();
        var turn = scheduler.PopNext();

        Assert.Equal(BattleScheduledEventKind.Interrupt, interrupt.Kind);
        Assert.Equal(BattleSide.Player, turn.Side);
        Assert.Equal(12, scheduler.Elapsed);
        Assert.Equal(0, scheduler.Count);
    }

    [Theory]
    [InlineData(BattleActionDelayKind.NormalTurn, 1000)]
    [InlineData(BattleActionDelayKind.RunAway, 250)]
    [InlineData(BattleActionDelayKind.BlastTwoSeconds, 2000)]
    [InlineData(BattleActionDelayKind.BlastFourSeconds, 4000)]
    [InlineData(BattleActionDelayKind.RandomStatusExpiry, 6000)]
    [InlineData(BattleActionDelayKind.BoostExpiry, 4000)]
    public void CalculateUsesTheQualifiedDelayFamily(BattleActionDelayKind kind, int expected)
    {
        int delay = BattleActionDelay.Calculate(kind, ownSpeed: 100, otherSpeed: 100, spirit: 100, new SequenceRng(7003));

        Assert.Equal(expected, delay);
    }

    [Theory]
    [InlineData(BattleActionDelayKind.Paralysis)]
    [InlineData(BattleActionDelayKind.Confusion)]
    [InlineData(BattleActionDelayKind.Sleep)]
    public void StatusExpiryWithoutTypedInputsIsRejected(BattleActionDelayKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleActionDelay.Calculate(kind, ownSpeed: 100, otherSpeed: 100, spirit: 100, new SequenceRng(7)));
    }

    [Fact]
    public void OpeningTraceOrdersFastAndSlowPlayersFromTheirInjectedRolls()
    {
        var fastPlayer = new BattleTurnScheduler();
        fastPlayer.InitializeOpening(playerSpeed: 200, enemySpeed: 100, ambushChance: 0, new SequenceRng(127));
        var slowPlayer = new BattleTurnScheduler();
        slowPlayer.InitializeOpening(playerSpeed: 100, enemySpeed: 200, ambushChance: 0, new SequenceRng(0));

        BattleScheduledEvent[] fastTrace = [fastPlayer.PopNext(), fastPlayer.PopNext()];
        BattleScheduledEvent[] slowTrace = [slowPlayer.PopNext(), slowPlayer.PopNext()];

        Assert.Equal([BattleSide.Player, BattleSide.Enemy], fastTrace.Select(item => item.Side));
        Assert.Equal([BattleSide.Enemy, BattleSide.Player], slowTrace.Select(item => item.Side));
        Assert.Equal(354, fastPlayer.Elapsed);
        Assert.Equal(707, slowPlayer.Elapsed);
    }

    [Fact]
    public void RearmedTurnUsesTheCurrentSpeedRatherThanTheOpeningSpeed()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 0);
        _ = scheduler.PopNext();

        scheduler.EnqueueActionTurn(BattleSide.Player, BattleActionDelayKind.NormalTurn, ownSpeed: 200, otherSpeed: 100, spirit: 0, new SequenceRng());

        Assert.Equal(709, scheduler.RemainingTurnDelay(BattleSide.Player));
    }

    [Fact]
    public void NinetyNineSlotsYieldTheReversePriorityTraceThenTheEuTurnTie()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 0);
        scheduler.EnqueueTurn(BattleSide.Enemy, delay: 0);
        var expected = new List<BattleSide>();

        for (var index = 0; index < 97; index++)
        {
            BattleSide side = index % 2 == 0 ? BattleSide.Player : BattleSide.Enemy;
            scheduler.EnqueuePriority(BattleScheduledEventKind.Interrupt, side, requestedDelay: 1);
            expected.Insert(0, side);
        }

        Assert.Equal(99, scheduler.Count);
        Assert.Throws<InvalidOperationException>(() => scheduler.EnqueuePriority(BattleScheduledEventKind.Interrupt, BattleSide.Player, requestedDelay: 1));

        var actual = new List<BattleSide>();
        while (scheduler.Count > 0)
        {
            actual.Add(scheduler.PopNext().Side);
        }

        expected.Add(BattleSide.Enemy);
        expected.Add(BattleSide.Player);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TraceIsInvariantAtEqualSimulatedTimeAcrossRenderCadences()
    {
        var steadyRng = new RecordingRng("scheduler-opening", 127);
        var unevenRng = new RecordingRng("scheduler-opening", 127);

        var steady = RunAtRenderCadence([16], simulatedTime: 5000, steadyRng);
        var uneven = RunAtRenderCadence([7, 23, 11, 31, 5, 19], simulatedTime: 5000, unevenRng);

        Assert.Equal(steady, uneven);
        Assert.Equal(["scheduler-opening:127"], steadyRng.Draws);
        Assert.Equal(steadyRng.Draws, unevenRng.Draws);
    }

    [Fact]
    public void InterruptionConsumesOnlyItsOwnSlotAndDoesNotDuplicateTheRearmedTurn()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 0);
        scheduler.EnqueueTurn(BattleSide.Enemy, delay: 50);
        scheduler.EnqueuePriority(BattleScheduledEventKind.Interrupt, BattleSide.Enemy, requestedDelay: 1);

        BattleScheduledEvent interrupt = scheduler.PopNext();
        BattleScheduledEvent player = scheduler.PopNext();
        scheduler.EnqueueActionTurn(BattleSide.Player, BattleActionDelayKind.NormalTurn, ownSpeed: 200, otherSpeed: 100, spirit: 0, new SequenceRng());
        BattleScheduledEvent enemy = scheduler.PopNext();
        BattleScheduledEvent rearmedPlayer = scheduler.PopNext();

        Assert.Equal(BattleScheduledEventKind.Interrupt, interrupt.Kind);
        Assert.Equal(BattleSide.Player, player.Side);
        Assert.Equal(BattleSide.Enemy, enemy.Side);
        Assert.Equal(BattleSide.Player, rearmedPlayer.Side);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void FailedPlayerFleeRearmsWithTheRunAwayDelay()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed
        {
            Id = 1,
            Hp = 100,
            MaxHp = 100,
            Mp = 0,
            MaxMp = 0,
            Stats = Stats(speed: 100),
        };
        setup.Enemies[0] = new BattleEnemy
        {
            Fighter = 2,
            Hp = 100,
            Mp = 0,
            Strength = 16,
            BaseStats = Stats(speed: 100),
        };
        var state = BattleState.InitFighters(setup, digimon: 1);

        bool fled = state.PlayerFlee(new SequenceRng(100));

        Assert.False(fled);
        Assert.Equal(250, state.NextPlayerTurn);
    }

    [Fact]
    public void EqualNonTurnWinsOverAnEarlierTurnAndBlocksALaterEqualTurn()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 10);
        AddEvent(scheduler, new BattleScheduledEvent(BattleScheduledEventKind.Interrupt, BattleSide.Player, 10));
        scheduler.EnqueueTurn(BattleSide.Enemy, delay: 10);

        Assert.Equal(BattleScheduledEventKind.Interrupt, scheduler.PopNext().Kind);
        Assert.Equal(BattleSide.Enemy, scheduler.PopNext().Side);
        Assert.Equal(BattleSide.Player, scheduler.PopNext().Side);
        Assert.Equal(10, scheduler.Elapsed);
    }

    [Fact]
    public void PoppedSlotIsReusedBeforeALaterEqualTurn()
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueTurn(BattleSide.Player, delay: 0);
        scheduler.EnqueueTurn(BattleSide.Enemy, delay: 10);
        Assert.Equal(BattleSide.Player, scheduler.PopNext().Side);

        scheduler.EnqueueTurn(BattleSide.Player, delay: 10);

        Assert.Equal(BattleSide.Enemy, scheduler.PopNext().Side);
        Assert.Equal(BattleSide.Player, scheduler.PopNext().Side);
        Assert.Equal(10, scheduler.Elapsed);
    }

    private static void AddEvent(BattleTurnScheduler scheduler, BattleScheduledEvent item)
    {
        var add = typeof(BattleTurnScheduler).GetMethod("Add", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing scheduler insertion seam.");
        _ = add.Invoke(scheduler, [item]);
    }

    private static List<string> RunAtRenderCadence(IReadOnlyList<int> frameCadence, int simulatedTime, IBattleRng rng)
    {
        var scheduler = new BattleTurnScheduler();
        scheduler.InitializeOpening(playerSpeed: 200, enemySpeed: 100, ambushChance: 0, rng);
        var trace = new List<string>();
        var accumulated = 0;
        var frame = 0;

        while (accumulated < simulatedTime)
        {
            accumulated = Math.Min(simulatedTime, accumulated + frameCadence[frame % frameCadence.Count]);
            frame++;
            ConsumeDueEvents(scheduler, accumulated, trace);
        }

        return trace;
    }

    private static void ConsumeDueEvents(BattleTurnScheduler scheduler, int accumulated, List<string> trace)
    {
        while (scheduler.Count > 0 && scheduler.Elapsed + scheduler.NextDueDelay <= accumulated)
        {
            BattleScheduledEvent next = scheduler.PopNext();
            trace.Add($"{next.Side}@{scheduler.Elapsed}");
            scheduler.EnqueueActionTurn(
                next.Side,
                BattleActionDelayKind.NormalTurn,
                next.Side == BattleSide.Player ? 200 : 100,
                next.Side == BattleSide.Player ? 100 : 200,
                spirit: 0,
                new SequenceRng());
        }
    }

    private static BattleStats Stats(int speed)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Speed] = speed;
        return stats;
    }

    private sealed class RecordingRng(string consumer, params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public List<string> Draws { get; } = [];

        public int NextValue()
        {
            int value = _values.Dequeue();
            Draws.Add($"{consumer}:{value}");
            return value;
        }
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
