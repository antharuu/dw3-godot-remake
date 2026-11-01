using Dw3.Data;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Coordinates validation, relational persistence, and manifest projection for recovered FieldTalk data.
/// </summary>
public static class DialogueDomainWriter
{
    private const int SnapshotProgress = 4;
    private static readonly HashSet<ushort> NonDialogueModes = [0x204, 0x22D];

    /// <summary>Determines whether a recovered stage mode supports FieldTalk data.</summary>
    public static bool IsDialogueZone(StageDialogueAnalysis stage) => !NonDialogueModes.Contains(stage.Mode);

    /// <summary>Rejects duplicate stages and modes that cannot contain dialogue.</summary>
    public static void ValidateDialogueStages(IReadOnlyList<StageDialogueAnalysis> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        StageDialogueAnalysis? invalid = stages.FirstOrDefault(stage => !IsDialogueZone(stage));
        if (invalid is not null)
        {
            throw new ArgumentException($"Stage mode 0x{invalid.Mode:X3} (WSTAG{invalid.StageId:D3}) is not a dialogue zone.", nameof(stages));
        }

        if (stages.Select(stage => stage.StageId).Distinct().Count() != stages.Count)
        {
            throw new ArgumentException("Dialogue stages contain a duplicate stage id.", nameof(stages));
        }
    }

    /// <summary>Writes the same dialogue projection to SQLite and the JSON manifest.</summary>
    public static void Write(DataPackWriteContext context, IReadOnlyList<StageDialogueAnalysis> stages, ProgressConditionTables progress, int snapshotProgress = SnapshotProgress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(progress);
        ValidateDialogueStages(stages);
        DialogueSqlWriter.Write(context, stages, progress, snapshotProgress);
        DialogueManifestWriter.WriteManifest(context, stages, progress, snapshotProgress);
    }
}
