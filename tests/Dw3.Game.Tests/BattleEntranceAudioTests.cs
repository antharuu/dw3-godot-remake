using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleEntranceAudioTests
{
    [Fact]
    public void Entrance_CueFiresOnceAtTheAuthoredBoundary_AndCancellationDisarmsIt()
    {
        var played = new List<AudioProgramEntry>();
        var clock = new BattleEntranceAudio(played.Add);
        var note = new AudioProgramEntry(2, "prepared.wav", 100, 64, 60, 0);
        clock.Begin(60, note);
        clock.Advance(59);
        Assert.Empty(played);
        clock.Advance(1);
        Assert.Same(note, Assert.Single(played));
        clock.Advance(120);
        Assert.Single(played);
        clock.Begin(60, note);
        clock.Cancel();
        clock.Advance(120);
        Assert.Single(played);
    }
}
