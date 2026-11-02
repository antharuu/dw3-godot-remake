using System.Globalization;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the field collision fixture from the supported disc.
/// Reads FieldActorEntry records from the S200 and S235 overlay files at
/// fixed overlay-relative addresses, then emits collision/collision-manifest.json.
///
/// Overlay load base address: 0x800A5DE0 (disc executable constant).
/// Supported overlay actor addresses (overlay RAM addresses):
///   S200: actor1 = 0x800A7110
///   S235: actor0 = 0x800A6524, actor1 = 0x800A6538.
/// </summary>
internal static class CollisionExtractionStep
{
    // The overlay RAM base address is constant across all supported stage overlays.
    // Disc format constant: OVERLAY_DATA_BASE in the field overlay layout.
    private const uint OverlayLoadBase = 0x800A5DE0u;

    // Per-stage actor record RAM addresses, verified from the EU disc overlays.
    // These are FIELDSTG_actorEntry addresses derived from the disc data section.
    private static readonly IReadOnlyDictionary<string, Dictionary<string, uint>> ActorAddresses =
        new Dictionary<string, Dictionary<string, uint>>
        {
            ["S200"] = new() { ["actor1"] = 0x800A7110u },
            ["S235"] = new() { ["actor0"] = 0x800A6524u, ["actor1"] = 0x800A6538u },
        };

    /// <summary>
    /// Decodes FieldActor records from the S200 and S235 stage overlays and writes
    /// <c>collision/collision-manifest.json</c> to the data pack at <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="cuePath">Path to the CUE sheet of the supported disc image.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="onlyDomain">When non-null, must equal <c>"collision"</c>; other values return exit 2.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on extraction error, 2 on usage error.</returns>
    internal static int Extract(string cuePath, string outputDirectory, string? onlyDomain, TextWriter output, TextWriter error)
    {
        if (onlyDomain is not null && !string.Equals(onlyDomain, "collision", StringComparison.Ordinal))
        {
            error.WriteLine($"Unsupported extraction domain: {onlyDomain}");
            return 2;
        }

        try
        {
            using var disc = DiscSource.Open(cuePath);
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();

            var actors = new Dictionary<string, Dictionary<string, FieldActor>>();
            foreach (var (stageName, stageActors) in ActorAddresses)
            {
                int stageNum = int.Parse(stageName[1..], CultureInfo.InvariantCulture);
                DiscFileEntry overlayEntry = RequireFile(entries, $"AAA/PRO/WSTAG{stageNum:D3}.PRO", stageName);
                byte[] overlay = disc.ReadFile(overlayEntry);
                var decoded = new Dictionary<string, FieldActor>();
                foreach (var (actorName, actorAddress) in stageActors)
                {
                    decoded[actorName] = FieldActorDecoder.Decode(overlay, actorAddress, OverlayLoadBase);
                }
                actors[stageName] = decoded;
            }

            var writer = new DataPackWriter(outputDirectory);
            writer.Write(new DataPackMetadata(
                typeof(CollisionExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                disc.IdentifyEdition().BinSha1,
                disc.IdentifyEdition().Label,
                DateTimeOffset.UtcNow), context =>
            {
                string manifestPath = context.GetImagePath("collision/collision-manifest.json");
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                using var manifestWriter = new StreamWriter(manifestPath);
                CollisionManifestWriter.WriteManifest(manifestWriter, actors);
            });

            output.WriteLine("{\"event\":\"domain-complete\",\"domain\":\"collision\",\"stages\":2}");
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Returns the first disc entry whose ISO path equals <paramref name="isoPath"/>,
    /// throwing <see cref="InvalidDataException"/> when no such entry exists.
    /// </summary>
    /// <param name="entries">The full disc file entry list to search.</param>
    /// <param name="isoPath">The ISO path to match exactly (ordinal comparison).</param>
    /// <param name="context">A human-readable stage name used in the error message.</param>
    /// <returns>The matching <see cref="DiscFileEntry"/>.</returns>
    /// <exception cref="InvalidDataException">The required disc member is absent.</exception>
    private static DiscFileEntry RequireFile(IReadOnlyList<DiscFileEntry> entries, string isoPath, string context)
    {
        return entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, isoPath, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc file missing for {context}: {isoPath}");
    }
}
