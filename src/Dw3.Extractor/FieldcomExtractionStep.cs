using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Field;
using Dw3.Formats.Water;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the field connection cue and icon domains from the supported disc.
///
/// Sources: <c>AAA/DAT/FIELD/FIELDCOM.BIN</c> (sprite archive with four entries) and
/// <c>AAA/PRO/FIELDSTG.PRO</c> (animation behaviour table).
///
/// Domain <c>"cue"</c>: emits the connection-cue animation frames used when the player
/// approaches a warp exit, built from archive entries 0, 2 and 3 plus the inner sequences
/// decoded from FIELDSTG.PRO.
///
/// Domain <c>"icon"</c>: emits the above-head connection indicator frames and their
/// icon animation sequences.
/// </summary>
internal static class FieldcomExtractionStep
{
    /// <summary>
    /// Opens the disc through <paramref name="openDisc"/>, reads FIELDCOM.BIN and FIELDSTG.PRO,
    /// decodes the requested domain's frames and sequences, and writes a data pack to
    /// <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="openDisc">Opens the disc that holds FIELDCOM.BIN and FIELDSTG.PRO; disposed before returning.</param>
    /// <param name="outputDirectory">Root directory for the written data pack.</param>
    /// <param name="domain">Either <c>"cue"</c> (warp-exit connection animation) or <c>"icon"</c> (above-head indicator).</param>
    /// <param name="output">Receives one JSON progress line on success.</param>
    /// <param name="error">Receives a plain-text error message on failure.</param>
    /// <returns>0 on success, 1 on any disc or decoding error.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, string domain, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            var entries = disc.ListFiles();
            byte[] fieldcom = disc.ReadFile(Require(entries, "AAA/DAT/FIELD/FIELDCOM.BIN"));
            byte[] fieldstg = disc.ReadFile(Require(entries, "AAA/PRO/FIELDSTG.PRO"));
            FieldcomArchive archive = FieldcomArchiveDecoder.Decode(fieldcom);
            SpriteSheet sheet = SpriteSheetDecoder.Decode(archive.Entries[0]);
            FieldcomVram vram = FieldcomVramDecoder.Decode([archive.Entries[2], archive.Entries[3]]);
            IReadOnlyList<FieldcomFrameSequence> inner = FieldcomBehaviorDecoder.DecodeCueInnerSequences(fieldstg);
            IReadOnlyList<FieldcomAnimation> animations = FieldcomBehaviorDecoder.DecodeIconAnimations(fieldstg, sheet.Frames.Keys.ToHashSet());
            var cueFrames = inner.SelectMany(x => x.Steps).Select(x => x.Frame)
                .Append(0x32).Append(0x33).Append(0x34).Append(0x35).Append(0x36).Append(0x37)
                .Distinct().Order().Select(id => DecodeFrame(id, sheet, vram)).ToArray();
            var iconFrames = animations.SelectMany(x => x.Steps).Select(x => x.Frame)
                .Distinct().Order().Select(id => DecodeFrame(id, sheet, vram)).ToArray();
            string hash = Convert.ToHexStringLower(SHA256.HashData(fieldcom));
            FieldcomCueDomain? cue = domain == "cue"
                ? new(
                    "AAA/DAT/FIELD/FIELDCOM.BIN",
                    hash,
                    fieldcom.Length,
                    cueFrames,
                    Enumerable.Range(0, 8).Select(direction => new FieldcomExitRow(direction, (direction / 2) + 6)).ToArray(),
                    inner.Select((x, index) => new FieldcomNamedSequence($"row-{x.Steps[0].Frame:X2}", x.Steps)).ToArray(),
                    [
                        new FieldcomNamedSequence("opening", cueFrames.Where(x => x.FrameId is >= 0x32 and <= 0x35).Select(x => new FieldcomFrameStep(x.FrameId, 1)).ToArray()),
                        new FieldcomNamedSequence("closing", cueFrames.Where(x => x.FrameId is >= 0x35 and <= 0x37).Select(x => new FieldcomFrameStep(x.FrameId, 1)).ToArray()),
                    ])
                : null;
            FieldcomIconDomain? icon = domain == "icon"
                ? new("AAA/DAT/FIELD/FIELDCOM.BIN", hash, fieldcom.Length, iconFrames, animations)
                : null;
            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(
                    typeof(FieldcomExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    edition.BinSha1,
                    edition.Label,
                    DateTimeOffset.UtcNow),
                context => FieldcomDomainWriter.Write(context, cue, icon));
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{domain}\",\"frames\":{(domain == "cue" ? cueFrames.Length : iconFrames.Length)}}}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Returns the first entry in <paramref name="entries"/> whose ISO path equals <paramref name="path"/>.
    /// </summary>
    /// <param name="entries">Flat list of disc file entries from <see cref="Dw3.Disc.DiscSource.ListFiles"/>.</param>
    /// <param name="path">ISO 9660 path to match, case-sensitive (e.g. <c>AAA/DAT/FIELD/FIELDCOM.BIN</c>).</param>
    /// <returns>The matching entry.</returns>
    /// <exception cref="InvalidDataException">No entry with the given path was found.</exception>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(x => x.IsoPath == path) ?? throw new InvalidDataException($"Required disc file missing: {path}");

    /// <summary>
    /// Decodes one sprite frame from the FIELDCOM sheet and VRAM: looks up frame <paramref name="id"/>
    /// in the sprite sheet, composites its parts against the CLUT data in <paramref name="vram"/>,
    /// and converts the resulting RGB15 words to 32-bit RGBA, treating colour zero as fully transparent.
    /// </summary>
    /// <param name="id">Frame index within the sprite sheet (0-based, from FIELDCOM.BIN archive entry 0).</param>
    /// <param name="sheet">Decoded FIELDCOM sprite sheet providing part descriptors and per-frame part lists.</param>
    /// <param name="vram">Decoded FIELDCOM VRAM providing the CLUT data for colour lookup.</param>
    /// <returns>The composited frame with its pixel dimensions, draw offsets, ABR flag, and RGBA data.</returns>
    /// <exception cref="FormatException">The sheet does not contain a frame with the given id.</exception>
    private static FieldcomExtractedFrame DecodeFrame(int id, SpriteSheet sheet, FieldcomVram vram)
    {
        if (!sheet.Frames.TryGetValue(id, out var raw))
        {
            throw new FormatException($"FIELDCOM sheet lacks frame 0x{id:X2}");
        }

        FieldcomSpriteImage image = FieldcomSpriteCompositor.Compose(sheet.Parts, raw.Span, vram);
        byte[] rgba = new byte[image.Words.Length * 4];
        for (int i = 0; i < image.Words.Length; i++)
        {
            ushort w = image.Words[i];
            rgba[i * 4] = (byte)((w & 31) * 255 / 31);
            rgba[(i * 4) + 1] = (byte)(((w >> 5) & 31) * 255 / 31);
            rgba[(i * 4) + 2] = (byte)(((w >> 10) & 31) * 255 / 31);
            rgba[(i * 4) + 3] = w == 0 ? (byte)0 : (byte)255;
        }

        return new FieldcomExtractedFrame(id, image.Width, image.Height, image.OffsetX, image.OffsetY, 0, image.Abr, rgba);
    }
}
