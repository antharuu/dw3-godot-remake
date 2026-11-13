using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleEntranceSequenceTests
{
    [Fact]
    public void PlayerEntrance_FollowsTheSourceTimeline()
    {
        var stage = new RecordingEntranceStage();
        var entrance = new BattleEntranceSequence(enemy: false, BattleWeakIdleRule.Normal, stage);

        int frames = PlayToCompletion(entrance, stage);

        Assert.Equal(309, frames);
        Assert.Equal(
            [
                "1:camera fighter enemy=False view=10 frames=60",
                "1:cue",
                "30:opponent visible=False",
                "125:swap motion=13",
                "125:camera fighter enemy=False view=10 frames=0",
                "309:camera enemy-view frames=60",
                "309:idle 0",
                "309:opponent visible=True",
            ],
            stage.Calls);
    }

    [Fact]
    public void EnemyEntrance_UsesTheEnemyViewAndCutsBack()
    {
        var stage = new RecordingEntranceStage();
        var entrance = new BattleEntranceSequence(enemy: true, BattleWeakIdleRule.Weak, stage);

        PlayToCompletion(entrance, stage);

        Assert.Contains("1:camera fighter enemy=True view=2 frames=60", stage.Calls);
        Assert.Contains("125:camera fighter enemy=True view=2 frames=0", stage.Calls);
        Assert.Contains("309:camera enemy-view frames=1", stage.Calls);
        Assert.Contains("309:idle 1", stage.Calls);
    }

    [Fact]
    public void Flash_RisesFromFrame61_HoldsWhiteAtTheSwap_AndIsGoneBeforeTheHold()
    {
        var stage = new RecordingEntranceStage();
        var entrance = new BattleEntranceSequence(enemy: false, BattleWeakIdleRule.Normal, stage);

        PlayToCompletion(entrance, stage);

        Assert.All(stage.Levels.Take(60), level => Assert.Equal(0, level));
        Assert.Equal(4, stage.Levels[60]);
        Assert.Equal(0xFF, stage.Levels[123]);
        Assert.Equal(0xFF, stage.Levels[124]);
        Assert.Equal(251, stage.Levels[125]);
        Assert.Equal(0, stage.Levels[188]);
        Assert.All(stage.Levels.Skip(189), level => Assert.Equal(0, level));
    }

    [Fact]
    public void ModelIsNeverReplacedBeforeFullWhite()
    {
        var stage = new RecordingEntranceStage();
        var entrance = new BattleEntranceSequence(enemy: false, BattleWeakIdleRule.Normal, stage);

        PlayToCompletion(entrance, stage);

        int swapFrame = int.Parse(stage.Calls.Single(call => call.EndsWith(":swap motion=13", StringComparison.Ordinal)).Split(':')[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(0xFF, stage.Levels[swapFrame - 2]);
        Assert.Equal(0xFF, stage.Levels[swapFrame - 1]);
        Assert.All(stage.Levels.Take(swapFrame - 2), level => Assert.True(level < 0xFF));
    }

    [Fact]
    public void CompletedEntrance_IgnoresFurtherSteps()
    {
        var stage = new RecordingEntranceStage();
        var entrance = new BattleEntranceSequence(enemy: false, BattleWeakIdleRule.Normal, stage);
        PlayToCompletion(entrance, stage);
        int calls = stage.Calls.Count;

        entrance.Step();

        Assert.Equal(calls, stage.Calls.Count);
        Assert.True(entrance.IsComplete);
    }

    [Fact]
    public void InvalidIdleSelection_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleEntranceSequence(false, 2, new RecordingEntranceStage()));
    }

    [Theory]
    [InlineData(25, 0, 100, 1)]
    [InlineData(26, 0, 100, 0)]
    [InlineData(50, 25, 100, 1)]
    [InlineData(20, -10, 100, 0)]
    [InlineData(25, 0, 103, 1)]
    [InlineData(26, 0, 103, 0)]
    public void WeakIdleRule_UsesTheIntegerQuarterAfterTheShownDamage(int hp, int damage, int maxHp, int expected)
    {
        Assert.Equal(expected, BattleWeakIdleRule.Select(hp, damage, maxHp));
    }

    [Fact]
    public void View_KeepsTheOutgoingStatusUntilTheSwap_AndHidesTheMessage()
    {
        var stage = new RecordingEntranceStage();
        var entrance = new BattleEntranceSequence(enemy: false, BattleWeakIdleRule.Normal, stage);
        BattleView view = new(
            BattleFlowPhase.EnemyTurn,
            BattleOutcome.None,
            [Fighter(10, active: false), Fighter(20, active: true)],
            [Fighter(30, active: true)],
            "switch",
            null,
            MessageVisible: true);

        BattleView before = BattleEntranceView.Apply(view, entrance, outgoingId: 10);
        Assert.False(before.MessageVisible);
        Assert.Equal(10, Assert.Single(before.Players, fighter => fighter.Active).Id);
        Assert.Equal(30, Assert.Single(before.Enemies, fighter => fighter.Active).Id);

        while (!entrance.Swapped)
        {
            entrance.Step();
        }

        BattleView after = BattleEntranceView.Apply(view, entrance, outgoingId: 10);
        Assert.False(after.MessageVisible);
        Assert.Equal(20, Assert.Single(after.Players, fighter => fighter.Active).Id);
    }

    [Fact]
    public void View_HoldsTheOutgoingEnemyForAnEnemyEntrance()
    {
        var entrance = new BattleEntranceSequence(enemy: true, BattleWeakIdleRule.Normal, new RecordingEntranceStage());
        BattleView view = new(
            BattleFlowPhase.PlayerCommand,
            BattleOutcome.None,
            [Fighter(10, active: true)],
            [Fighter(30, active: false), Fighter(40, active: true)],
            "enemy",
            null);

        BattleView held = BattleEntranceView.Apply(view, entrance, outgoingId: 30);

        Assert.Equal(30, Assert.Single(held.Enemies, fighter => fighter.Active).Id);
        Assert.Equal(10, Assert.Single(held.Players, fighter => fighter.Active).Id);
    }

    private static BattleFighterView Fighter(int id, bool active) => new(id, null, 100, 100, 10, 10, active, KnockedOut: false);

    private static int PlayToCompletion(BattleEntranceSequence entrance, RecordingEntranceStage stage)
    {
        for (int frame = 1; frame <= 1000; frame++)
        {
            stage.Frame = frame;
            entrance.Step();
            if (entrance.IsComplete)
            {
                return frame;
            }
        }

        throw new InvalidOperationException("entrance did not complete");
    }

    private sealed class RecordingEntranceStage : IBattleEntranceStage
    {
        public int Frame { get; set; }

        public List<string> Calls { get; } = [];

        public List<int> Levels { get; } = [];

        public void MoveCameraToFighter(bool enemy, int viewIndex, int frames) => Calls.Add($"{Frame}:camera fighter enemy={enemy} view={viewIndex} frames={frames}");

        public void MoveCameraToEnemyView(int frames) => Calls.Add($"{Frame}:camera enemy-view frames={frames}");

        public void BeginEntranceCue() => Calls.Add($"{Frame}:cue");

        public void SetOpponentVisible(bool visible) => Calls.Add($"{Frame}:opponent visible={visible}");

        public void SwapInModel(int motion) => Calls.Add($"{Frame}:swap motion={motion}");

        public void SetEnteringIdle(int idleMotion) => Calls.Add($"{Frame}:idle {idleMotion}");

        public void PresentFlash(int level) => Levels.Add(level);
    }
}
