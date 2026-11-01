using Xunit;

namespace Dw3.Game.Tests;

using Dw3Production.Presentation;

public sealed class LegacyExitCueConsoleTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<(int Frame, int Duration)>> Rows =
        new Dictionary<int, IReadOnlyList<(int Frame, int Duration)>>
        {
            [6] = [(0x10, 22), (0x11, 22)],
            [7] = [(0x0E, 22), (0x0F, 22)],
            [8] = [(0x0C, 22), (0x0D, 22)],
            [9] = [(0x12, 22), (0x13, 22)],
            [10] = [(0x45, 22), (0x46, 22)],
        };

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        StartsHiddenAndOpensFromSourcePopRange();
        UsesRawDirectionRowsAndSourceDurations();
        ClosesThenHidesWhenOfferEnds();
        RestartsCleanlyOnRepeatedOfferAndSceneReset();
        RejectsInvalidDirectionsAndRows();
    }

    private static void StartsHiddenAndOpensFromSourcePopRange()
    {
        var state = new ExitCueState(Rows);
        Check(state.Advance(false, 3).Phase == ExitCuePhase.Hidden, "no offer remains hidden");
        var first = state.Advance(true, 3);
        Check(first.Phase == ExitCuePhase.Opening && first.PopFrame == 0x32 && first.InnerFrame is null, "offer begins pop at source 0xC8 >> 2 without inner sprite");
        ExitCueSnapshot opened = first;
        for (var tick = 0; tick < 20 && opened.Phase != ExitCuePhase.Open; tick++) opened = state.Advance(true, 3);
        Check(opened.Phase == ExitCuePhase.Open && opened.PopFrame == 0x35 && opened.InnerFrame == 0x0E, "opening reaches source 0xD4 >> 2 then reveals direction row");
    }

    private static void UsesRawDirectionRowsAndSourceDurations()
    {
        var state = new ExitCueState(Rows);
        ExitCueSnapshot snapshot = state.Advance(true, 4);
        for (var tick = 0; tick < 20 && snapshot.Phase != ExitCuePhase.Open; tick++) snapshot = state.Advance(true, 4);
        Check(snapshot.InnerFrame == 0x45, "raw direction 4 selects source row 10");
        for (var tick = 0; tick < 23; tick++) snapshot = state.Advance(true, 4);
        Check(snapshot.InnerFrame == 0x46, "source 22-unit duration advances to second frame");
    }

    private static void ClosesThenHidesWhenOfferEnds()
    {
        var state = new ExitCueState(Rows);
        for (var tick = 0; tick < 20; tick++) state.Advance(true, 3);
        var closing = state.Advance(false, 3);
        Check(closing.Phase == ExitCuePhase.Closing && closing.PopFrame == 0x35 && closing.InnerFrame is null, "offer loss starts source closing at 0xD4 without inner sprite");
        ExitCueSnapshot hidden = closing;
        for (var tick = 0; tick < 20 && hidden.Phase != ExitCuePhase.Hidden; tick++) hidden = state.Advance(false, 3);
        Check(hidden.Phase == ExitCuePhase.Hidden && !hidden.Visible, "closing reaches source 0xDC and removes cue");
    }

    private static void RestartsCleanlyOnRepeatedOfferAndSceneReset()
    {
        var state = new ExitCueState(Rows);
        for (var tick = 0; tick < 20; tick++) state.Advance(true, 3);
        for (var tick = 0; tick < 20; tick++) state.Advance(false, 3);
        var repeat = state.Advance(true, 7);
        Check(repeat.Phase == ExitCuePhase.Opening && repeat.PopFrame == 0x32 && repeat.RawDirection == 7, "repeat offer restarts opening with its raw direction");
        state.Reset();
        Check(state.Advance(false, 0).Phase == ExitCuePhase.Hidden, "scene reset removes prior cue lifecycle");
    }

    private static void RejectsInvalidDirectionsAndRows()
    {
        var state = new ExitCueState(Rows);
        CheckThrows<ArgumentOutOfRangeException>(() => state.Advance(true, 8), "invalid raw direction is rejected");
        var malformed = new ExitCueState(new Dictionary<int, IReadOnlyList<(int Frame, int Duration)>>());
        CheckThrows<InvalidOperationException>(() =>
        {
            for (var tick = 0; tick < 20; tick++) malformed.Advance(true, 3);
        }, "missing cue direction row is rejected");
    }

    private static void CheckThrows<T>(Action action, string name) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"FAIL: {name}");
    }

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
