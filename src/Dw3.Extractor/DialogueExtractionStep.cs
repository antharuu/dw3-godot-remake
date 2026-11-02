using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Dialogue;

namespace Dw3.Extractor;

/// <summary>Extracts every dialogue-bearing stage from the user disc into SQLite and manifest JSON.</summary>
internal static class DialogueExtractionStep
{
    /// <summary>
    /// Runs the dialogue extraction pipeline against the disc at <paramref name="cuePath"/> and writes
    /// the SQLite rows and manifest JSON to <paramref name="outputDirectory"/>. Stages whose mode is
    /// not a dialogue zone (modes 0x204 and 0x22D) are silently omitted; unexpected failures for
    /// non-0x210 modes are treated as fatal and propagated as <see cref="FormatException"/>.
    /// </summary>
    /// <param name="cuePath">Path to the CUE sheet of the supported disc image.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on extraction error.</returns>
    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            RomAnalysisResult analysis = DiscDialogueAnalysisReader.Read(disc, entries);
            StageAnalysisException[] unexpected = [.. analysis.Failures.Where(failure => failure.Mode != 0x210)];
            if (unexpected.Length != 0)
                throw new FormatException(string.Join(Environment.NewLine, unexpected.Select(item => item.Message)));

            StageDialogueAnalysis[] stages = [.. analysis.Stages
                .Where(DialogueDomainWriter.IsDialogueZone)
                .OrderBy(stage => stage.StageId)];
            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(
                    typeof(DialogueExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    edition.BinSha1,
                    edition.Label,
                    DateTimeOffset.UtcNow),
                context => DialogueDomainWriter.Write(context, stages, analysis.Progress));

            int actors = stages.Sum(stage => stage.Actors.Count);
            int talkTables = stages.Sum(stage => stage.Actors.Count);
            int selected = actors; // snapshot selection was evaluated for every actor, including null results.
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"dialogue\",\"zones\":{stages.Length},\"actors\":{actors},\"talk_tables\":{talkTables},\"selected\":{selected}}}");
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error (dialogue): {exception.Message}");
            return 1;
        }
    }
}
