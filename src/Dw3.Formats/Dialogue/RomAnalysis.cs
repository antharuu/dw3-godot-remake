using Dw3.Formats.Binary;
using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

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
    /// <summary>Analyses every supplied overlay and preserves failures that cannot be recovered safely.</summary>
    /// <param name="executable">The disc executable.</param>
    /// <param name="layout">Addresses of the supported disc.</param>
    /// <param name="files">The executable file-sector table joined with the disc directory.</param>
    /// <param name="countryFolder">Country folder of the language copy to resolve (for example <c>FRA</c>).</param>
    /// <param name="actorWidths">Per-character widths from the field width table.</param>
    /// <param name="stages">The stage overlays of the field-stage registry.</param>
    /// <returns>The executable progress data plus recovered and failed stage analyses.</returns>
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
    /// <param name="stage">Overlay whose dialogue setup is decoded.</param>
    /// <param name="layout">Addresses of the supported disc.</param>
    /// <param name="resolver">Resolver for the language-specific FieldTalk file.</param>
    /// <param name="actorWidths">Per-character field width table.</param>
    /// <returns>Dialogue inputs recovered without guessing.</returns>
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
                {
                    throw new FormatException($"Actor {index} at 0x{address:X8} has character id {entry.ActorId}, outside the {actorWidths.Count}-entry width table.");
                }

                IReadOnlyList<FieldTalk> talks = FieldTalkReader.Read(stage.Data, entry.TalksPtr, layout.StageOverlayLoadAddress);
                actors.Add(new StageActorDialogue(index, address, entry, actorWidths[entry.ActorId], talks));
            }
        }

        return new StageDialogueAnalysis(stage.Mode, stage.StageId, textFile, setup.ActorList?.Address, actors);
    }

    /// <summary>Decodes the executable tables used by dialogue progress conditions.</summary>
    /// <param name="executable">The disc executable containing the tables.</param>
    /// <param name="layout">Addresses and record count of the supported disc.</param>
    /// <returns>Progress ranges and special records bounded by the range table.</returns>
    private static ProgressConditionTables ReadProgress(PsxExecutable executable, DialogueRomLayout layout)
    {
        int rangesOffset = executable.OffsetOf(layout.ProgressRangesAddress, layout.ProgressRangeCount * 2);
        int specialOffset = executable.OffsetOf(layout.SpecialConditionsAddress, 0);

        // The special records are stored directly before the range table and must end before it.
        if (layout.SpecialConditionsAddress >= layout.ProgressRangesAddress)
        {
            throw new FormatException("Special-condition table must precede the progress-range table.");
        }

        return ProgressConditionDecoder.Decode(
            executable.Bytes, rangesOffset, layout.ProgressRangeCount, specialOffset, rangesOffset);
    }
}
