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
    /// <param name="stage">The stage dialogue analysis record to test.</param>
    /// <returns><see langword="true"/> when the stage mode is not in the non-dialogue exclusion set.</returns>
    public static bool IsDialogueZone(StageDialogueAnalysis stage) => !NonDialogueModes.Contains(stage.Mode);

    /// <summary>Rejects duplicate stages and modes that cannot contain dialogue.</summary>
    /// <param name="stages">The list of stage dialogue analyses to validate; must contain no duplicate stage ids
    /// and no stages whose mode is in the non-dialogue exclusion set (0x204, 0x22D).</param>
    /// <exception cref="ArgumentException">A stage uses a non-dialogue mode or a stage id appears more than once.</exception>
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
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="stages">The analysed stage dialogue records; must pass <see cref="ValidateDialogueStages"/>.</param>
    /// <param name="progress">The progress condition tables for conditional talk selection.</param>
    /// <param name="snapshotProgress">The snapshot progress value used for condition evaluation; defaults to 4.</param>
    /// <exception cref="ArgumentException">Validation fails for <paramref name="stages"/>.</exception>
    public static void Write(DataPackWriteContext context, IReadOnlyList<StageDialogueAnalysis> stages, ProgressConditionTables progress, int snapshotProgress = SnapshotProgress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(progress);
        ValidateDialogueStages(stages);
        DialogueSqlWriter.Write(context, stages, progress, snapshotProgress);
        DialogueManifestWriter.WriteManifest(context, stages, progress, snapshotProgress);
    }
}
