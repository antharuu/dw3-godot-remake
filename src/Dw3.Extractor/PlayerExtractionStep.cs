using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Binary.Tim;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the player sprite domain from the supported disc.
///
/// Reads P002PLAY.BIN from AAA/DAT/FIELD/SPRT/P002PLAY.BIN, decodes all 87
/// TIM 4-bit frame sprites (archive entries 0x00..0x56) and the animation-set
/// table (archive entry 0x70), then emits:
/// <list type="bullet">
///   <item><c>player/frames/frame-XX.png</c> × 87 (two-digit hex index)</item>
///   <item><c>player/manifest.json</c> matching the Python parity baseline</item>
/// </list>
///
/// Source reference:
///   FIELD_CHARACTER_PLAYER = 2 → FIELDSTG_fileEntries[2] = 0x1870070
///   → file 0x187 → AAA/DAT/FIELD/SPRT/P002PLAY.BIN → archive entry 0x70
/// </summary>
internal static class PlayerExtractionStep
{
    // Source constants documented from the disc format.
    private const string CharacterLabel = "FIELD_CHARACTER_PLAYER = 2";
    private const string FileEntryLabel = "FIELDSTG_fileEntries[2] = 0x1870070";
    private const int FileNumber = 0x187;   // 391
    private const int ActorWidthPx = 0x20;    // 32
    private const int HalfWidthPx = 0x10;    // 16
    // FIELDSTG_drawActor: draw offset applied to every player frame.
    private const int AnchorOffsetX = -16;
    private const int AnchorOffsetY = -28;

    internal static int Extract(
        string cuePath,
        string outputDirectory,
        string? onlyDomain,
        TextWriter output,
        TextWriter error)
    {
        if (onlyDomain is not null && !string.Equals(onlyDomain, "player", StringComparison.Ordinal))
        {
            error.WriteLine($"Unsupported extraction domain: {onlyDomain}");
            return 2;
        }

        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();

            DiscFileEntry archiveEntry = RequireFile(entries, PlayerArchiveDecoder.PlayerArchiveDiscPath);
            byte[] archiveBytes = disc.ReadFile(archiveEntry);
            string archiveSha256 = ComputeSha256Hex(archiveBytes);

            var writer = new DataPackWriter(outputDirectory);
            writer.Write(new DataPackMetadata(
                typeof(PlayerExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow), context =>
            {
                WritePlayerDomain(context, archiveBytes, archiveSha256, archiveEntry.IsoPath);
            });

            output.WriteLine("{\"event\":\"domain-complete\",\"domain\":\"player\",\"frames\":87}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {ex.Message}");
            return 1;
        }
    }

    private static void WritePlayerDomain(
        DataPackWriteContext context,
        byte[] archiveBytes,
        string archiveSha256,
        string archiveIsoPath)
    {
        // ── Decode offset table ─────────────────────────────────────────────
        int[] offsets = PlayerArchiveDecoder.DecodeOffsetTable(
            archiveBytes, PlayerArchiveDecoder.PlayerAnimSetTableEntry);

        // ── Decode 87 TIM frames ─────────────────────────────────────────────
        string framesDir = "player/frames";
        var frameInfos = new FrameInfo[PlayerArchiveDecoder.PlayerFrameCount];

        for (int frameIndex = 0; frameIndex < PlayerArchiveDecoder.PlayerFrameCount; frameIndex++)
        {
            int start = offsets[frameIndex];
            int end = offsets[frameIndex + 1];
            TimImage timImage = TimDecoder.Decode(archiveBytes.AsSpan(start, end - start));

            string relPath = $"{framesDir}/frame-{frameIndex:X2}.png";
            string absPath = context.GetImagePath(relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
            File.WriteAllBytes(absPath, PngEncoder.Encode(timImage.Width, timImage.Height, timImage.Pixels));
            context.RegisterImage(relPath);

            PlayerFrameBbox bbox = PlayerArchiveDecoder.ComputeVisibleBbox(timImage);
            frameInfos[frameIndex] = new FrameInfo(timImage.Width, timImage.Height, bbox);
        }

        // ── Decode animation-set table ───────────────────────────────────────
        IReadOnlyList<PlayerAnimSetGroup> setGroups = PlayerArchiveDecoder.DecodeAnimSetTable(
            archiveBytes, offsets, PlayerArchiveDecoder.PlayerAnimSetTableEntry);

        // ── Decode all sequences + compute per-record derived fields ─────────
        var decodedSets = new List<DecodedSet>();
        var footAnchors = new List<(int OffsetX, int OffsetY, int Width, int Height)>();
        var groundContacts = new HashSet<(double X, double Y)>();

        foreach (PlayerAnimSetGroup group in setGroups)
        {
            var decodedSequences = new List<DecodedSequence>();
            for (int direction = 0; direction < group.SequenceIndices.Count; direction++)
            {
                int seqIndex = group.SequenceIndices[direction];
                PlayerAnimSequence seq = PlayerArchiveDecoder.DecodeSequence(
                    archiveBytes, offsets, seqIndex, PlayerArchiveDecoder.PlayerFrameCount);

                var decodedRecords = new List<DecodedRecord>();
                foreach (PlayerAnimRecord record in seq.Records)
                {
                    FrameInfo fi = frameInfos[record.Frame];
                    // Compute foot_ground_contact using the Python formula (includes off-by-one):
                    //   contact_x = offset_x + (left + right_excl + 1) / 2.0
                    //   contact_y = offset_y + lower_excl + 1
                    double contactX = record.OffsetX + (fi.Bbox.Left + fi.Bbox.RightExclusive + 1) / 2.0;
                    double contactY = record.OffsetY + fi.Bbox.LowerExclusive + 1;
                    groundContacts.Add((contactX, contactY));
                    footAnchors.Add((record.OffsetX, record.OffsetY, fi.Width, fi.Height));
                    decodedRecords.Add(new DecodedRecord(record, fi, contactX, contactY));
                }
                decodedSequences.Add(new DecodedSequence(direction, seqIndex, seq.Terminator, decodedRecords));
            }
            decodedSets.Add(new DecodedSet(group.SetId, PlayerArchiveDecoder.GetSetName(group.SetId), decodedSequences));
        }

        // ── Build anchor_variants ────────────────────────────────────────────
        var anchorCounts = new Dictionary<(int Ox, int Oy, int W, int H), int>();
        foreach (var a in footAnchors)
        {
            var key = (a.OffsetX, a.OffsetY, a.Width, a.Height);
            anchorCounts.TryGetValue(key, out int count);
            anchorCounts[key] = count + 1;
        }
        var anchorVariants = anchorCounts
            .OrderBy(kv => kv.Key.Ox).ThenBy(kv => kv.Key.Oy)
            .ThenBy(kv => kv.Key.W).ThenBy(kv => kv.Key.H)
            .ToList();

        // ── Build ground_contact_variants ────────────────────────────────────
        var sortedContacts = groundContacts
            .OrderBy(c => c.X).ThenBy(c => c.Y)
            .ToList();

        // ── Write manifest.json ───────────────────────────────────────────────
        string manifestPath = context.GetImagePath("player/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        using var manifestStream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write);
        using var jsonWriter = new Utf8JsonWriter(manifestStream, new JsonWriterOptions { Indented = true });

        WriteManifest(
            jsonWriter,
            archiveIsoPath,
            archiveSha256,
            archiveBytes.Length,
            frameInfos,
            decodedSets,
            sortedContacts,
            anchorVariants);
    }

    private static void WriteManifest(
        Utf8JsonWriter w,
        string archiveIsoPath,
        string archiveSha256,
        int archiveBytes,
        FrameInfo[] frames,
        List<DecodedSet> sets,
        List<(double X, double Y)> groundContacts,
        List<KeyValuePair<(int Ox, int Oy, int W, int H), int>> anchorVariants)
    {
        w.WriteStartObject();   // root
        w.WriteStartObject("player");

        // anchor_variants  (alphabetical key order matches Python baseline)
        w.WriteStartArray("anchor_variants");
        foreach (var kv in anchorVariants)
        {
            w.WriteStartArray();
            w.WriteStartArray();
            w.WriteNumberValue(kv.Key.Ox);
            w.WriteNumberValue(kv.Key.Oy);
            w.WriteNumberValue(kv.Key.W);
            w.WriteNumberValue(kv.Key.H);
            w.WriteEndArray();
            w.WriteNumberValue(kv.Value);
            w.WriteEndArray();
        }
        w.WriteEndArray();

        // frames  (keys are decimal integers as strings, ascending)
        w.WriteStartObject("frames");
        for (int i = 0; i < frames.Length; i++)
        {
            FrameInfo fi = frames[i];
            w.WriteStartObject(i.ToString());
            w.WriteStartArray("size");
            w.WriteNumberValue(fi.Width);
            w.WriteNumberValue(fi.Height);
            w.WriteEndArray();
            w.WriteStartArray("visible_bbox");
            w.WriteNumberValue(fi.Bbox.Left);
            w.WriteNumberValue(fi.Bbox.Upper);
            w.WriteNumberValue(fi.Bbox.RightExclusive);
            w.WriteNumberValue(fi.Bbox.LowerExclusive);
            w.WriteEndArray();
            w.WriteStartArray("visible_size");
            w.WriteNumberValue(fi.Bbox.VisibleSizeWidth);
            w.WriteNumberValue(fi.Bbox.VisibleSizeHeight);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndObject();

        // render_anchor
        w.WriteStartObject("render_anchor");
        w.WriteStartArray("ground_contact_variants");
        foreach (var (cx, cy) in groundContacts)
        {
            w.WriteStartArray();
            w.WriteNumberValue(cx);
            w.WriteNumberValue((int)cy);  // y is always integer (offset_y + lower_excl + 1)
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteString("note",
            "FIELDSTG_drawActor draws the whole 32x32 TIM with its top-left at (tile + offset); " +
            "the player's ground contact is the bottom-centre of the visible pixels, not the TIM's bottom edge.");
        w.WriteStartArray("offset");
        w.WriteNumberValue(AnchorOffsetX);
        w.WriteNumberValue(AnchorOffsetY);
        w.WriteEndArray();
        w.WriteStartArray("tim_size");
        w.WriteNumberValue(ActorWidthPx);
        w.WriteNumberValue(ActorWidthPx);
        w.WriteEndArray();
        w.WriteEndObject();

        // sets
        w.WriteStartArray("sets");
        foreach (DecodedSet ds in sets)
        {
            w.WriteStartObject();
            w.WriteString("name", ds.Name);
            w.WriteStartArray("sequences");
            foreach (DecodedSequence seq in ds.Sequences)
            {
                w.WriteStartObject();
                w.WriteNumber("direction", seq.Direction);
                w.WriteNumber("index", seq.Index);
                w.WriteStartArray("records");
                foreach (DecodedRecord rec in seq.Records)
                {
                    w.WriteStartObject();
                    w.WriteNumber("duration", rec.Source.Duration);
                    w.WriteStartArray("foot_ground_contact");
                    w.WriteNumberValue(rec.ContactX);
                    w.WriteNumberValue((int)rec.ContactY);
                    w.WriteEndArray();
                    w.WriteNumber("frame", rec.Source.Frame);
                    w.WriteStartArray("frame_size");
                    w.WriteNumberValue(rec.Frame.Width);
                    w.WriteNumberValue(rec.Frame.Height);
                    w.WriteEndArray();
                    w.WriteStartArray("offset");
                    w.WriteNumberValue(rec.Source.OffsetX);
                    w.WriteNumberValue(rec.Source.OffsetY);
                    w.WriteEndArray();
                    w.WriteStartArray("visible_size");
                    w.WriteNumberValue(rec.Frame.Bbox.VisibleSizeWidth);
                    w.WriteNumberValue(rec.Frame.Bbox.VisibleSizeHeight);
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteString("terminator", seq.Terminator == PlayerAnimSequenceTerminator.Loop ? "loop" : "end");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("set", ds.SetId);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // source
        w.WriteStartObject("source");
        w.WriteNumber("actor_width_pixels", ActorWidthPx);
        w.WriteNumber("archive_entry", PlayerArchiveDecoder.PlayerAnimSetTableEntry);
        w.WriteNumber("bytes", archiveBytes);
        w.WriteString("character", CharacterLabel);
        w.WriteString("file_entry", FileEntryLabel);
        w.WriteNumber("file_number", FileNumber);
        w.WriteNumber("half_width_pixels", HalfWidthPx);
        w.WriteString("path", archiveIsoPath);
        w.WriteString("sha256", archiveSha256);
        w.WriteEndObject();

        w.WriteEndObject(); // player
        w.WriteEndObject(); // root
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ComputeSha256Hex(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    private static DiscFileEntry RequireFile(IReadOnlyList<DiscFileEntry> entries, string isoPath)
    {
        return entries.SingleOrDefault(e => string.Equals(e.IsoPath, isoPath, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc file missing: {isoPath}");
    }

    // ── Private record types ──────────────────────────────────────────────────

    private readonly record struct FrameInfo(int Width, int Height, PlayerFrameBbox Bbox);

    private sealed record DecodedSet(int SetId, string Name, List<DecodedSequence> Sequences);

    private sealed record DecodedSequence(
        int Direction,
        int Index,
        PlayerAnimSequenceTerminator Terminator,
        List<DecodedRecord> Records);

    private sealed record DecodedRecord(
        PlayerAnimRecord Source,
        FrameInfo Frame,
        double ContactX,
        double ContactY);
}
