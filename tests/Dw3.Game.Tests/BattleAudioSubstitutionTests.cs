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

    [Theory]
    [InlineData(false, true, false, 3, 0)]
    [InlineData(true, false, false, 0, 3)]
    [InlineData(false, true, true, 3, 2)]
    [InlineData(true, true, true, 0, 2)]
    public void DoubleMagic_FreezesBothSoundSlotsIncludingTerminalMissAndKo(
        bool firstLanded, bool lastLanded, bool defeated, int firstMarker, int finalMarker)
    {
        var tech = new TechData { Script = 11, ScriptSound = 53, Effect = BattleTechniqueEffect.DoubleMagic };
        BattleHitEvent[] hits =
        [
            new(BattleSide.Player, 0, firstLanded, firstLanded ? 10 : 0, tech.Effect, false),
            new(BattleSide.Player, 1, lastLanded, lastLanded ? 10 : 0, tech.Effect, false) { TargetDefeated = defeated },
        ];
        var context = ActionSequenceContext.TryCapture(BattleSide.Player, tech, 10, 20, hits)!;
        Assert.Equal([firstMarker, 0, 0, finalMarker], context.SoundResults);
        var script = new BattleActionScript(
            11,
            [
                new(BattleActionOpcode.Sound, [0x62, 0]),
                new(BattleActionOpcode.Sound, [0x63, 0]),
                new(BattleActionOpcode.End, []),
            ]);
        var timeline = new BattleActionTimeline(script, context);
        Assert.Equal(
            [firstMarker == 3 ? 0x38 : 53, finalMarker == 3 ? 0x38 : 53],
            timeline.Advance().OfType<SoundRequestEvent>().Select(sound => sound.SoundId));
    }

    [Fact]
    public void DoubleMagic_EarlyLethalHitMovesTheOnlyLandedSoundToTerminalSlot()
    {
        var tech = new TechData { Script = 11, ScriptSound = 53, Effect = BattleTechniqueEffect.DoubleMagic };
        BattleHitEvent[] hits = [new(BattleSide.Player, 0, true, 1, tech.Effect, false) { TargetDefeated = true }];
        var context = ActionSequenceContext.TryCapture(BattleSide.Player, tech, 10, 20, hits)!;
        Assert.Equal([3, 0, 0, 2], context.SoundResults);
    }

    [Fact]
    public void FatalHit_UsesKoMarker_NotOrdinaryLandedMarker()
    {
        var tech = new TechData { Script = 5, ScriptSound = 53 };
        BattleHitEvent[] hits = [new(BattleSide.Player, 0, true, 1, 0, false) { TargetDefeated = true }];
        var context = ActionSequenceContext.TryCapture(BattleSide.Player, tech, 10, 20, hits)!;
        Assert.Equal(2, context.SoundResults[3]);
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
