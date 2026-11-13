using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks each production tick of an entrance call against the source caller and entrance.</summary>
internal static class BattleEntranceTickChecks
{
    /// <summary>Checks one tick according to the call phase it ended in.</summary>
    /// <param name="sample">Observed tick.</param>
    /// <param name="expected">Expected entrance.</param>
    /// <param name="heldFlow">Flow phase held since the call started.</param>
    public static void Check(BattleEntranceTickSample sample, BattleEntranceExpectation expected, string heldFlow)
    {
        string where = $"{expected.Label} tick={sample.Tick} phase={sample.Phase} frame={sample.Frame}";
        if (sample.Phase != nameof(BattleEntranceCallPhase.Done))
        {
            ProbeRequirement.Require(sample.Flow == heldFlow, $"{where}: flow left the held phase {heldFlow} -> {sample.Flow}");
        }

        switch (sample.Phase)
        {
            case nameof(BattleEntranceCallPhase.Before):
                CheckBefore(sample, expected, where);
                break;
            case nameof(BattleEntranceCallPhase.Sequence):
                CheckSequence(sample, expected, where);
                break;
            case nameof(BattleEntranceCallPhase.After):
                CheckAfter(sample, expected, where);
                break;
            default:
                CheckReleased(sample, expected, where);
                break;
        }
    }

    /// <summary>The caller's first message: the scene is untouched and the camera free.</summary>
    /// <param name="sample">Observed tick.</param>
    /// <param name="expected">Expected entrance.</param>
    /// <param name="where">Failure prefix.</param>
    private static void CheckBefore(BattleEntranceTickSample sample, BattleEntranceExpectation expected, string where)
    {
        ProbeRequirement.Require(expected.BeforeKey is { } key && sample.Message.Contains(key, StringComparison.Ordinal), $"{where}: before message={sample.Message} expected={expected.BeforeKey}");
        ProbeRequirement.Require(!sample.Owned && sample.Frame == 0 && sample.Rendered == expected.OutgoingId && sample.OpponentVisible, $"{where}: scene changed before the entrance owned={sample.Owned} rendered={sample.Rendered}");
    }

    /// <summary>The entrance: no message, outgoing model until full white, opponent hidden from frame 30.</summary>
    /// <param name="sample">Observed tick.</param>
    /// <param name="expected">Expected entrance.</param>
    /// <param name="where">Failure prefix.</param>
    private static void CheckSequence(BattleEntranceTickSample sample, BattleEntranceExpectation expected, string where)
    {
        bool swapped = sample.Frame >= BattleEntranceSequence.LeadFrames + 65;
        bool messageExpected = swapped && expected.AfterAtSwap;
        ProbeRequirement.Require(
            sample.Owned && (messageExpected ? sample.Message.Contains(expected.AfterKey, StringComparison.Ordinal) : sample.Message.Length == 0),
            $"{where}: message={sample.Message} owned={sample.Owned} expected at swap={messageExpected}");
        ProbeRequirement.Require(sample.Rendered == (swapped ? expected.IncomingId : expected.OutgoingId), $"{where}: rendered={sample.Rendered}");
        ProbeRequirement.Require(sample.Hud == (swapped ? expected.IncomingId : expected.OutgoingId), $"{where}: hud={sample.Hud}");
        ProbeRequirement.Require(sample.OpponentVisible == sample.Frame < BattleEntranceSequence.HideOpponentFrame, $"{where}: opponent visible={sample.OpponentVisible}");
        ProbeRequirement.Require(sample.Frame != 126 || sample.Motion == BattleEntranceSequence.EntranceMotion, $"{where}: incoming motion={sample.Motion}");
    }

    /// <summary>The caller's post message: the camera stays owned while the box waits for Cross.</summary>
    /// <param name="sample">Observed tick.</param>
    /// <param name="expected">Expected entrance.</param>
    /// <param name="where">Failure prefix.</param>
    private static void CheckAfter(BattleEntranceTickSample sample, BattleEntranceExpectation expected, string where)
    {
        ProbeRequirement.Require(sample.Message.Contains(expected.AfterKey, StringComparison.Ordinal), $"{where}: after message={sample.Message} expected={expected.AfterKey}");
        ProbeRequirement.Require(sample.Owned && sample.OpponentVisible && sample.Rendered == expected.IncomingId, $"{where}: owned={sample.Owned} opponent={sample.OpponentVisible} rendered={sample.Rendered}");
    }

    /// <summary>The released battle: camera and visibility returned, the caller's box closed.</summary>
    /// <param name="sample">Observed tick.</param>
    /// <param name="expected">Expected entrance.</param>
    /// <param name="where">Failure prefix.</param>
    private static void CheckReleased(BattleEntranceTickSample sample, BattleEntranceExpectation expected, string where) =>
        ProbeRequirement.Require(
            !sample.Owned && sample.OpponentVisible && sample.Rendered == expected.IncomingId && !sample.Message.Contains(expected.AfterKey, StringComparison.Ordinal),
            $"{where}: release owned={sample.Owned} opponent={sample.OpponentVisible} rendered={sample.Rendered} message={sample.Message}");
}
