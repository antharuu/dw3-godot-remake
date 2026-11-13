using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>Extracts action sprites independently of unrelated prepared domains.</summary>
internal static class SpriteEffectExtractionStep
{
    /// <summary>Runs the separate disc-to-prepared sprite producer.</summary>
    /// <param name="openDisc">User-disc opener.</param>
    /// <param name="outputDirectory">Prepared pack root.</param>
    /// <param name="output">Completion events.</param>
    /// <param name="error">Extraction failures.</param>
    /// <returns>Zero on successful publication, one on malformed input.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            byte[] executable = disc.ReadFile(entries.Single(entry => entry.IsoPath == "SLES_039.36"));
            byte[] overlay = disc.ReadFile(entries.Single(entry => entry.IsoPath == "AAA/PRO/FIGHTSTG.PRO"));
            PsxExecutable parsed = PsxExecutable.Parse(executable);
            var files = new EngineFileReader(disc, entries, parsed);
            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata("0.1.0", edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
                context =>
                {
                    SpriteEffectDomainWriter.Write(context, overlay, files);
                    SpriteEffectScriptWriter.Write(context, files, parsed);
                });
            output.WriteLine("{\"event\":\"domain-complete\",\"domain\":\"battle-sprites\"}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [battle-sprites]: {ex.Message}");
            return 1;
        }
    }
}
