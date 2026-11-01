using Dw3.Formats.Binary;
using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>A WSTAG overlay to analyse: its field mode, stage number and raw file bytes.</summary>
public sealed record StageOverlay(ushort Mode, int StageId, byte[] Data);

/// <summary>One actor of a stage with the address of its entry and of its FieldTalk table.</summary>
/// <param name="Index">Position in the stage actor pointer list (the actor's id within the stage).</param>
/// <param name="Address">RAM address of the actor entry.</param>
/// <param name="Entry">The decoded actor entry.</param>
/// <param name="Width">Talk/collision width of the actor's character, from the field width table.</param>
/// <param name="Talks">The actor's FieldTalk alternatives, read at <see cref="NpcActorEntry.TalksPtr"/>.</param>
public sealed record StageActorDialogue(int Index, uint Address, NpcActorEntry Entry, int Width, IReadOnlyList<FieldTalk> Talks);

/// <summary>The dialogue inputs recovered for one stage overlay.</summary>
/// <param name="ActorListAddress">RAM address of the actor pointer list, or null for a stage without actors.</param>
public sealed record StageDialogueAnalysis(
    ushort Mode,
    int StageId,
    StageTextFile TextFile,
    uint? ActorListAddress,
    IReadOnlyList<StageActorDialogue> Actors);

/// <summary>A stage whose dialogue setup cannot be recovered without guessing.</summary>
public sealed class StageAnalysisException(ushort mode, int stageId, Exception inner)
    : FormatException($"Stage mode 0x{mode:X3} (WSTAG{stageId:D3}): {inner.Message}", inner)
{
    public ushort Mode { get; } = mode;
    public int StageId { get; } = stageId;
}

/// <summary>The disc-wide analysis: executable progress metadata and every stage, analysed or failed.</summary>
public sealed record RomAnalysisResult(
    ProgressConditionTables Progress,
    IReadOnlyList<StageDialogueAnalysis> Stages,
    IReadOnlyList<StageAnalysisException> Failures);

/// <summary>
/// Recovers, from the disc alone, the inputs field dialogue needs: per-stage talk file, actor and
/// FieldTalk table addresses, and the executable's progress ranges and special conditions.
/// </summary>
/// <remarks>
/// A stage that cannot be analysed is reported as a <see cref="StageAnalysisException"/> naming its mode
/// and overlay, never filled with an empty or plausible default. The other stages are still analysed.
/// Executable-level tables are required: their failure aborts the analysis.
/// </remarks>
public static class RomAnalysis
{
    /// <param name="executable">The disc executable.</param>
    /// <param name="layout">Addresses of the supported disc.</param>
    /// <param name="files">The executable file-sector table joined with the disc directory.</param>
    /// <param name="countryFolder">Country folder of the language copy to resolve (for example <c>FRA</c>).</param>
    /// <param name="actorWidths">Per-character widths from the field width table.</param>
    /// <param name="stages">The stage overlays of the field-stage registry.</param>
    public static RomAnalysisResult Analyze(
        PsxExecutable executable,
        DialogueRomLayout layout,
        DiscFileTable files,
        string countryFolder,
        IReadOnlyList<int> actorWidths,
        IEnumerable<StageOverlay> stages)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(actorWidths);
        ArgumentNullException.ThrowIfNull(stages);

        ProgressConditionTables progress = ReadProgress(executable, layout);
        var resolver = new StageTextFileResolver(files, layout.LanguageCopiesPerTextFile, countryFolder);
        var analysed = new List<StageDialogueAnalysis>();
        var failures = new List<StageAnalysisException>();
        foreach (StageOverlay stage in stages)
        {
            try
            {
                analysed.Add(AnalyzeStage(stage, layout, resolver, actorWidths));
            }
            catch (FormatException error)
            {
                failures.Add(new StageAnalysisException(stage.Mode, stage.StageId, error));
            }
        }
        return new RomAnalysisResult(progress, analysed, failures);
    }

    /// <summary>Analyses one overlay. Throws a <see cref="FormatException"/> when any part is undecidable.</summary>
    public static StageDialogueAnalysis AnalyzeStage(
        StageOverlay stage,
        DialogueRomLayout layout,
        StageTextFileResolver resolver,
        IReadOnlyList<int> actorWidths)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(resolver);
        StageDialogueSetup setup = StageDialogueScanner.Scan(stage.Data, layout);
        StageTextFile textFile = resolver.Resolve(setup.TextSelector);
        var actors = new List<StageActorDialogue>();
        if (setup.ActorList is not null)
        {
            for (int index = 0; index < setup.ActorList.Actors.Count; index++)
            {
                uint address = setup.ActorList.Actors[index];
                NpcActorEntry entry = NpcOverlayReader.ReadActorEntry(stage.Data, address, layout.StageOverlayLoadAddress);
                if (entry.ActorId < 0 || entry.ActorId >= actorWidths.Count)
                    throw new FormatException($"Actor {index} at 0x{address:X8} has character id {entry.ActorId}, outside the {actorWidths.Count}-entry width table.");
                IReadOnlyList<FieldTalk> talks = FieldTalkReader.Read(stage.Data, entry.TalksPtr, layout.StageOverlayLoadAddress);
                actors.Add(new StageActorDialogue(index, address, entry, actorWidths[entry.ActorId], talks));
            }
        }
        return new StageDialogueAnalysis(stage.Mode, stage.StageId, textFile, setup.ActorList?.Address, actors);
    }

    private static ProgressConditionTables ReadProgress(PsxExecutable executable, DialogueRomLayout layout)
    {
        int rangesOffset = executable.OffsetOf(layout.ProgressRangesAddress, layout.ProgressRangeCount * 2);
        int specialOffset = executable.OffsetOf(layout.SpecialConditionsAddress, 0);
        // The special records are stored directly before the range table and must end before it.
        if (layout.SpecialConditionsAddress >= layout.ProgressRangesAddress)
            throw new FormatException("Special-condition table must precede the progress-range table.");
        return ProgressConditionDecoder.Decode(
            executable.Bytes, rangesOffset, layout.ProgressRangeCount, specialOffset, rangesOffset);
    }
}
