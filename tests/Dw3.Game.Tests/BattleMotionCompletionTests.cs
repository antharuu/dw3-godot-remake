using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleMotionCompletionTests
{
    [Fact]
    public void CompletionSurvivesAutomaticIdleAndDoesNotCompleteTheNextRequest()
    {
        IReadOnlyList<IReadOnlyList<int>> idle = [[0, 4, 1, 4], [4, 0, -32768, 0], [32767, 0, 0, 0]];
        IReadOnlyList<IReadOnlyList<int>> action = [[0, 2, 10, 11], [2, 2, 0, 0], [4, 0, -1, 0], [32767, 0, 0, 0]];
        var motion = new BattleMotionMachine([idle, idle, action]);
        int before = motion.CompletionCount;
        motion.SetMotion(3, restart: true);
        for (int tick = 0; tick < 50; tick++)
        {
            motion.Step(1);
        }

        Assert.Equal(1, motion.Motion);
        Assert.False(motion.MotionDone);
        Assert.Equal(before + 1, motion.CompletionCount);
        int next = motion.CompletionCount;
        motion.SetMotion(3, restart: true);
        Assert.Equal(next, motion.CompletionCount);
        Assert.False(motion.MotionDone);
        motion.Step(1);
        Assert.Equal(next, motion.CompletionCount);
    }
}
