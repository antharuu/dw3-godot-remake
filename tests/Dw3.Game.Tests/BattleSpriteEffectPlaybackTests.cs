using System.Text.Json;
using Dw3.Data.Battle;
using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleSpriteEffectPlaybackTests
{
    [Fact]
    public void LoadThenStartAttachesOneDrawnChildAtTheScriptAnchor()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);

        Assert.Equal(SequenceAcknowledgement.Applied, playback.Consume(Load(7)));
        Assert.Empty(surface.Attached);
        Assert.Equal(SequenceAcknowledgement.Applied, playback.Consume(Start(7, 12, -34, 56)));

        var instance = Assert.Single(surface.Attached);
        Assert.Equal(7, instance.Id);
        Assert.Equal(new BattleSpriteAnchor(12, -34, 56), instance.Anchor);
        Assert.Equal(BattleSpriteLayer.Models, instance.Layer);
        Assert.False(playback.IsComplete);
        Assert.Equal(1, playback.StartedCount);
    }

    [Fact]
    public void IdenticalStartsAreDistinctInstancesWithIndependentLifetimes()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);
        playback.Consume(Start(7, 0, 0, 0));
        playback.Step();
        playback.Consume(Start(7, 0, 0, 0));

        Assert.Equal(2, surface.Attached.Count);
        Assert.NotSame(surface.Attached[0], surface.Attached[1]);
        Assert.NotEqual(surface.Attached[0].Serial, surface.Attached[1].Serial);
        Step(playback, 3);
        Assert.Equal([surface.Attached[0]], surface.Detached);
        Assert.Equal([surface.Attached[1]], playback.Active);
        playback.Step();
        Assert.Equal(surface.Attached, surface.Detached);
        Assert.True(playback.IsComplete);
    }

    [Fact]
    public void ChildrenAdvanceOneSourceSamplePerStepAndKeepZeroScaleTiming()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);
        playback.Consume(Start(7, 0, 0, 0));
        var instance = surface.Attached[0];

        Assert.Equal([1, 0], instance.DrawnSamples().Select(drawn => drawn.Animation));
        playback.Step();
        Assert.Equal([(1, (short)10), (0, (short)1)], instance.DrawnSamples().Select(drawn => (drawn.Animation, drawn.Sample.Frame)));
        playback.Step();
        Assert.Equal([(1, (short)11)], instance.DrawnSamples().Select(drawn => (drawn.Animation, drawn.Sample.Frame)));
        Assert.Equal([1, 1], instance.Times);
        playback.Step();
        Assert.Equal([(0, (short)1)], instance.DrawnSamples().Select(drawn => (drawn.Animation, drawn.Sample.Frame)));
        playback.Step();
        Assert.True(instance.IsComplete);
        Assert.Equal(2, instance.Sheet.Animations.Count);
    }

    [Fact]
    public void FramesKeepTheirSourceBlendModeAndDrawerTransform()
    {
        var sheet = BattleSpriteSheetParser.Parse(7, Payload(7), path => "/pack/" + path);

        Assert.False(sheet.Frames[(10, 0)].IsSemiTransparent);
        Assert.Equal(1, sheet.Frames[(1, 0)].Abr);
        Assert.Equal(3, sheet.Frames[(11, 0)].Abr);
        Assert.Equal(["/pack/battle/sprites/7/frame-1-row-0-part-0.png", "/pack/battle/sprites/7/frame-1-row-0-part-1.png"], sheet.Frames[(1, 0)].Parts.Select(part => part.Path));
        Assert.Equal("cleared-vram", sheet.Frames[(1, 0)].Parts[1].PaletteSource);
        var quarter = new BattleSpriteSample(1, 0, 0, 0, 4096, 8192, 0, 0, 1024).LinearTransform();
        Assert.Equal(0f, quarter.X.X, 4);
        Assert.Equal(1f, quarter.X.Y, 4);
        Assert.Equal(-2f, quarter.Y.X, 4);
        Assert.Equal(0f, quarter.Y.Y, 4);
        var tilted = new BattleSpriteSample(1, 0, 0, 0, 4096, 4096, 1024, 0, 0).LinearTransform();
        Assert.Equal(0f, tilted.Y.Y, 4);
        Assert.False(new BattleSpriteSample(1, 0, 0, 0, 0, 4096, 0, 0, 0).IsDrawn);
    }

    [Fact]
    public void WhiteFlashRisesHoldsThenFallsWithoutHoldingTheScript()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);
        Assert.Equal(SequenceAcknowledgement.Applied, playback.Consume(new ScreenFadeEvent([0, 4])));
        var levels = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            playback.Step();
            levels.Add(surface.FlashLevels[^1]);
        }

        Assert.Equal([63, 126, 189, 252, 255, 255], levels);
        Assert.True(playback.IsComplete);
        Assert.Equal(SequenceAcknowledgement.Applied, playback.Consume(new ScreenFadeEvent([1, 2])));
        Step(playback, 3);
        Assert.Equal([128, 1, 0], surface.FlashLevels.TakeLast(3));
        Assert.Empty(playback.Flashes);
        Assert.Equal(SequenceAcknowledgement.Unavailable, playback.Consume(new ScreenFadeEvent([0, 0])));
    }

    [Fact]
    public void CancellationDestroysEveryChildAndTheFlash()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);
        playback.Consume(Start(7, 0, 0, 0));
        playback.Consume(Start(1001, 0, 120, 0x7FFF));
        playback.Consume(new ScreenFadeEvent([0, 8]));
        playback.Step();

        playback.Clear();

        Assert.Equal(surface.Attached, surface.Detached);
        Assert.Equal(BattleSpriteLayer.Front, surface.Attached[1].Layer);
        Assert.True(surface.Attached[1].Anchor.IsScreen);
        Assert.Empty(playback.Active);
        Assert.Empty(playback.Flashes);
        Assert.Equal(0, surface.FlashLevels[^1]);
        Assert.Equal(0, playback.StartedCount);
    }

    [Fact]
    public void UnavailableIdentitiesAreNeverAcknowledgedAndSourceAbsenceEndsAtOnce()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);

        Assert.Equal(SequenceAcknowledgement.Unavailable, playback.Consume(Start(18, 0, 0, 0)));
        Assert.Equal(SequenceAcknowledgement.Unavailable, playback.Consume(Load(18)));
        Assert.Equal(SequenceAcknowledgement.Unavailable, playback.Consume(Start(99, 0, 0, 0)));
        Assert.Equal(SequenceAcknowledgement.Applied, playback.Consume(Start(40, 0, 0, 0)));
        Assert.Empty(surface.Attached);
    }

    [Fact]
    public void ANinthConcurrentEffectIsDroppedLikeTheSourceScript()
    {
        var surface = new RecordingSurface();
        var playback = Playback(surface);
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(SequenceAcknowledgement.Applied, playback.Consume(Start(7, 0, 0, 0)));
        }

        Assert.Equal(BattleSpriteEffectPlayback.MaxScriptEffects, surface.Attached.Count);
    }

    private static BattleSpriteEffectPlayback Playback(RecordingSurface surface)
    {
        var index = new Dictionary<int, PreparedSpriteEffect>
        {
            [7] = new(7, "available", Payload(7)),
            [1001] = new(1001, "available", Payload(1001)),
            [18] = new(18, "unsupported", JsonDocument.Parse("""{"reason":"palette outside loaded pages"}""").RootElement),
            [40] = new(40, "source-absent", JsonDocument.Parse("""{"reason":"zero archive entry"}""").RootElement),
        };
        var catalog = new BattleSpriteCatalog(index, path => "/pack/" + path);
        return new BattleSpriteEffectPlayback(() => catalog, surface);
    }

    private static SpriteEffectEvent Load(short id) => new(BattleSide.Player, [1, id]);

    private static SpriteEffectEvent Start(short id, short x, short y, short z) => new(BattleSide.Player, [0, id, x, y, z]);

    private static void Step(BattleSpriteEffectPlayback playback, int count)
    {
        for (int i = 0; i < count; i++)
        {
            playback.Step();
        }
    }

    private static JsonElement Payload(int id)
    {
        string Part(int frame, int index, string source) =>
            $$"""{"layer":{"path":"battle/sprites/{{id}}/frame-{{frame}}-row-0-part-{{index}}.png","width":8,"height":4},"dx":-4,"dy":-2,"paletteSource":"{{source}}"}""";
        string json = $$"""
            {
              "animations": [
                [[1,0,0,0,4096,4096,0,0,0],[1,0,0,0,0,4096,0,0,0],[1,0,2,3,4096,4096,0,0,0]],
                [[10,0,0,0,4096,4096,0,0,0],[11,0,0,0,4096,4096,0,0,0]],
                null,
                [[1,0,0,0,4096,4096,0,0,0]]
              ],
              "frames": [
                {"frame":1,"row":0,"abr":1,"parts":[{{Part(1, 0, "effect-page")}},{{Part(1, 1, "cleared-vram")}}]},
                {"frame":10,"row":0,"abr":-1,"parts":[{{Part(10, 0, "effect-page")}}]},
                {"frame":11,"row":0,"abr":3,"parts":[{{Part(11, 0, "effect-page")}}]}
              ]
            }
            """;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class RecordingSurface : IBattleSpriteSurface
    {
        public List<BattleSpriteEffectInstance> Attached { get; } = [];

        public List<BattleSpriteEffectInstance> Detached { get; } = [];

        public List<int> FlashLevels { get; } = [];

        public void Attach(BattleSpriteEffectInstance instance) => Attached.Add(instance);

        public void Detach(BattleSpriteEffectInstance instance) => Detached.Add(instance);

        public void Present(IReadOnlyList<BattleSpriteEffectInstance> active, int flashLevel) => FlashLevels.Add(flashLevel);
    }
}
