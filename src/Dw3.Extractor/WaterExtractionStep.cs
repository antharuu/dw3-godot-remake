using System.Globalization;
using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Water;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the map-object ("water") domain: for every field stage of the disc registry,
/// decodes the StageTile objects of <c>PRO/WSTAGnnn.PRO</c> with the sprite sheet of
/// <c>DAT/FIELD/STAGE/SnnnTMPK.BIN</c> and the object VRAM of <c>DAT/FIELD/STAGE/SnnnMASK.BIN</c>.
/// A stage whose files use a layout the decoder rejects is recorded as skipped with the
/// decoder's reason instead of failing the whole extraction.
/// </summary>
internal static class WaterExtractionStep
{
    private const string DomainName = "water";

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            var zones = new List<ExtractedWaterZone>();
            var skipped = new List<SkippedWaterZone>();
            foreach (ExtractedFieldStage stage in ZoneExtractionStep.BuildFieldRegistry(disc, entries))
            {
                int id = stage.StageId;
                byte[] overlay = disc.ReadFile(ZoneExtractionStep.RequireStageFile(entries, "AAA/PRO/WSTAG", ".PRO", id));
                byte[] sheet = disc.ReadFile(ZoneExtractionStep.RequireStageFile(entries, "AAA/DAT/FIELD/STAGE/S", "TMPK.BIN", id));
                byte[] texture = disc.ReadFile(ZoneExtractionStep.RequireStageFile(entries, "AAA/DAT/FIELD/STAGE/S", "MASK.BIN", id));
                try
                {
                    StageObjectSet objects = StageObjectDecoder.Decode(id, overlay, sheet, texture);
                    zones.Add(new ExtractedWaterZone(id, objects, new WaterZoneSources(Sha256(overlay), Sha256(sheet), Sha256(texture))));
                }
                catch (FormatException exception)
                {
                    skipped.Add(new SkippedWaterZone(id, exception.Message));
                    output.WriteLine($"{{\"event\":\"zone-skipped\",\"domain\":\"{DomainName}\",\"stage\":{id}}}");
                }
            }

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(WaterExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow), context => WaterDomainWriter.Write(context, zones, skipped));
            int frames = zones.Sum(zone => zone.Objects.Objects.Sum(item => item.Frames.Count));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{{\"event\":\"domain-complete\",\"domain\":\"{DomainName}\",\"stages\":{zones.Count},\"skipped\":{skipped.Count},\"frames\":{frames}}}"));
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
