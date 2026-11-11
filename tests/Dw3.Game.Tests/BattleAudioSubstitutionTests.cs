using Dw3.Data.Battle;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleAudioSubstitutionTests
{
    [Fact]
    public void HitSoundSubstitutions_UseFrozenTechniqueAndMarkers_WithoutRetryIncrement()
    {
        var context = new ActionSequenceContext(BattleSide.Player, 10, 20, [], 5)
        {
            ScriptSound = 53,
            SoundResults = [3, 0, 0, 3],
        };
        var script = new BattleActionScript(
            5,
            [
                new(BattleActionOpcode.Sound, [0x62, 4]),
                new(BattleActionOpcode.Sound, [0x62, 0]),
                new(BattleActionOpcode.Sound, [0x63, 0]),
                new(BattleActionOpcode.End, []),
            ]);
        var timeline = new BattleActionTimeline(script, context);
        BattleSequenceEvent? first = null;
        int attempts = 0;
        timeline.Acknowledge = request =>
        {
            first ??= request;
            if (attempts++ < 2)
            {
                Assert.Same(first, request);
                return false;
            }

            return true;
        };
        Assert.Empty(timeline.Advance());
        Assert.Empty(timeline.Advance());
        var sounds = timeline.Advance().OfType<SoundRequestEvent>().ToArray();
        Assert.Equal([0x38, 53, 0x38], sounds.Select(sound => sound.SoundId));
        Assert.Equal(4, sounds[0].Timing);
        Assert.True(timeline.IsComplete);
    }

    [Fact]
    public void Context_CopiesTechniqueSoundAndHitMarkers_WithoutRetainingMutableInput()
    {
        var tech = new TechData { Script = 5, ScriptSound = 53 };
        List<BattleHitEvent> hits = [new(BattleSide.Player, 0, false, 0, 0, false)];
        var context = ActionSequenceContext.TryCapture(BattleSide.Player, tech, 10, 20, hits);
        Assert.NotNull(context);
        hits.Clear();
        tech.ScriptSound = 1;
        Assert.Equal(53, context.ScriptSound);
        Assert.Equal(3, context.SoundResults[3]);
        Assert.Single(context.HitEvents);
    }
}
