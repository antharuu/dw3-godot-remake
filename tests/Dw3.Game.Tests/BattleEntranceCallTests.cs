using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>When the source callers open the message naming the incoming fighter, and when the call lets the battle
/// go on.</summary>
public sealed class BattleEntranceCallTests
{
    private const int SwapFrame = 125;

    private const int EnemyEntranceFrames = 189 + 120;

    [Fact]
    public void Replacement_OpensItsNameMessageOnlyAfterTheEntrance()
    {
        var call = NewCall(afterAtSwap: false);
        for (int frame = 1; frame <= EnemyEntranceFrames; frame++)
        {
            call.Step(confirm: true);
            if (frame < EnemyEntranceFrames)
            {
                Assert.Null(call.OpenMessage);
            }
        }

        Assert.Equal(BattleEntranceCallPhase.After, call.Phase);
        Assert.NotNull(call.OpenMessage);
    }

    [Fact]
    public void VoluntaryEnemySwitch_OpensItsNameMessageAtTheSwap()
    {
        var call = NewCall(afterAtSwap: true);
        for (int frame = 1; frame < SwapFrame; frame++)
        {
            call.Step(confirm: false);
            Assert.Null(call.OpenMessage);
        }

        call.Step(confirm: false);
        Assert.True(call.Sequence.Swapped);
        Assert.Equal(BattleEntranceCallPhase.Sequence, call.Phase);
        Assert.NotNull(call.OpenMessage);
    }

    [Fact]
    public void VoluntaryEnemySwitch_ClosedEarly_StillHoldsTheBattleUntilTheEntranceEnds()
    {
        var call = NewCall(afterAtSwap: true);
        int frame = 0;
        while (call.Phase != BattleEntranceCallPhase.Done)
        {
            frame++;
            call.Step(confirm: true);
            Assert.True(frame <= EnemyEntranceFrames, "the call never ended");
            if (!call.Sequence.IsComplete)
            {
                Assert.NotEqual(BattleEntranceCallPhase.Done, call.Phase);
            }
        }

        Assert.Equal(EnemyEntranceFrames, frame);
        Assert.Equal(EnemyEntranceFrames, call.Sequence.Frames);
        Assert.True(call.AfterFrames < EnemyEntranceFrames - SwapFrame);
    }

    [Fact]
    public void VoluntaryEnemySwitch_OpenPastTheEntrance_WaitsForCrossAfterIt()
    {
        var call = NewCall(afterAtSwap: true);
        for (int frame = 1; frame <= EnemyEntranceFrames; frame++)
        {
            call.Step(confirm: false);
        }

        Assert.Equal(BattleEntranceCallPhase.After, call.Phase);
        Assert.Equal(EnemyEntranceFrames - SwapFrame, call.AfterFrames);
        call.Step(confirm: true);
        Assert.Equal(BattleEntranceCallPhase.Done, call.Phase);
    }

    private static BattleEntranceCall NewCall(bool afterAtSwap) =>
        new(
            new BattleEntranceSequence(enemy: true, idleMotion: 0, new NullStage()),
            before: null,
            new BattleEntranceMessage(new BattleTextLine([], [], "message=enemyEntered"), 2),
            () => { },
            afterAtSwap);

    /// <summary>Scene that ignores every entrance operation.</summary>
    private sealed class NullStage : IBattleEntranceStage
    {
        public void MoveCameraToFighter(bool enemy, int viewIndex, int frames)
        {
        }

        public void MoveCameraToEnemyView(int frames)
        {
        }

        public void BeginEntranceCue()
        {
        }

        public void SetOpponentVisible(bool visible)
        {
        }

        public void SwapInModel(int motion)
        {
        }

        public void SetEnteringIdle(int idleMotion)
        {
        }

        public void PrepareEntranceExtras(BattleEntranceExtras extras)
        {
        }

        public void ApplyEntranceExtras(BattleEntranceExtras extras)
        {
        }

        public void PresentFlash(int level)
        {
        }
    }
}
