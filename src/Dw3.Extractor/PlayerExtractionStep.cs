using System.Globalization;
using System.Security.Cryptography;
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
///   → file 0x187 → AAA/DAT/FIELD/SPRT/P002PLAY.BIN → archive entry 0x70.
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

    /// <summary>
    /// Opens the disc, reads P002PLAY.BIN (file 0x187), and writes the player sprite domain
    /// to <paramref name="outputDirectory"/>. Returns 2 when <paramref name="onlyDomain"/> is set
    /// to a value other than <c>"player"</c>.
    /// </summary>
    /// <param name="openDisc">Opens the disc that holds the player sprite archives; disposed before returning.</param>
    /// <param name="outputDirectory">Root directory for the written data pack.</param>
    /// <param name="onlyDomain">When non-null, must equal <c>"player"</c>; any other value returns exit code 2.</param>
    /// <param name="output">Receives one JSON progress line on success.</param>
    /// <param name="error">Receives a plain-text error message on failure.</param>
    /// <returns>0 on success, 1 on disc or decoding error, 2 on unsupported domain.</returns>
    internal static int Extract(
        Func<IDiscSource> openDisc,
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
            using var disc = openDisc();
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

    /// <summary>
    /// Decodes the offset table and all 87 TIM frames from the archive, builds the animation-set
    /// table, and writes the manifest JSON and per-frame PNG files to the data pack.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="archiveBytes">Raw bytes of P002PLAY.BIN.</param>
    /// <param name="archiveSha256">Lowercase hex SHA-256 of <paramref name="archiveBytes"/>, for parity verification.</param>
    /// <param name="archiveIsoPath">ISO path of the archive file recorded in the manifest source section.</param>
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
        FrameInfo[] frameInfos = DecodeAndWriteFrames(context, archiveBytes, offsets);

        // ── Decode animation-set table ───────────────────────────────────────
        IReadOnlyList<PlayerAnimSetGroup> setGroups = PlayerArchiveDecoder.DecodeAnimSetTable(
            archiveBytes, offsets, PlayerArchiveDecoder.PlayerAnimSetTableEntry);

        // ── Decode sequences and build derived collections ───────────────────
        (List<DecodedSet> decodedSets,
            List<(int OffsetX, int OffsetY, int Width, int Height)> footAnchors,
            HashSet<(double X, double Y)> groundContacts) =
            DecodeAnimationSets(archiveBytes, offsets, frameInfos, setGroups);

        var anchorVariants = BuildAnchorVariants(footAnchors);
        var sortedContacts = groundContacts.OrderBy(c => c.X).ThenBy(c => c.Y).ToList();

        // ── Write manifest.json ───────────────────────────────────────────────
        string manifestPath = context.GetImagePath("player/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        using var manifestStream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write);
        using var jsonWriter = new Utf8JsonWriter(manifestStream, new JsonWriterOptions { Indented = true });

        var archiveRef = new PlayerArchiveRef(archiveIsoPath, archiveSha256, archiveBytes.Length);
        WriteManifest(jsonWriter, archiveRef, frameInfos, decodedSets, sortedContacts, anchorVariants);
    }

    /// <summary>
    /// Decodes all 87 TIM 4-bit frames from the archive (entries 0x00..0x56), encodes each as a PNG,
    /// writes it to <c>player/frames/frame-XX.png</c> in the data pack, and returns their pixel dimensions
    /// and visible bounding boxes for use in the manifest.
    /// </summary>
    /// <param name="context">Write context providing the image-path resolver and image registry.</param>
    /// <param name="archiveBytes">Raw bytes of the P002PLAY.BIN archive.</param>
    /// <param name="offsets">Per-entry byte offsets decoded from the archive's offset table (entry count + 1 entries).</param>
    /// <returns>Array of 87 <see cref="FrameInfo"/> values in frame-index order.</returns>
    private static FrameInfo[] DecodeAndWriteFrames(DataPackWriteContext context, byte[] archiveBytes, int[] offsets)
    {
        const string framesDir = "player/frames";
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

        return frameInfos;
    }

    /// <summary>
    /// Decodes every animation set from the archive's set table, computing per-record foot ground
    /// contacts using the same formula as the Python parity decoder
    /// (<c>contact_x = offset_x + (left + right_excl + 1) / 2.0</c>).
    /// </summary>
    /// <param name="archiveBytes">Raw bytes of the P002PLAY.BIN archive.</param>
    /// <param name="offsets">Per-entry byte offsets decoded from the archive's offset table.</param>
    /// <param name="frameInfos">Frame dimensions and bounding boxes in frame-index order.</param>
    /// <param name="setGroups">Animation set groups decoded from the archive's set table (entry 0x70).</param>
    /// <returns>A tuple of decoded sets, per-record foot anchors (offset + TIM size), and unique ground contact positions.</returns>
    private static (
        List<DecodedSet> Sets,
        List<(int OffsetX, int OffsetY, int Width, int Height)> FootAnchors,
        HashSet<(double X, double Y)> GroundContacts)
        DecodeAnimationSets(
            byte[] archiveBytes,
            int[] offsets,
            FrameInfo[] frameInfos,
            IReadOnlyList<PlayerAnimSetGroup> setGroups)
    {
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
                    double contactX = record.OffsetX + ((fi.Bbox.Left + fi.Bbox.RightExclusive + 1) / 2.0);
                    double contactY = record.OffsetY + fi.Bbox.LowerExclusive + 1;
                    groundContacts.Add((contactX, contactY));
                    footAnchors.Add((record.OffsetX, record.OffsetY, fi.Width, fi.Height));
                    decodedRecords.Add(new DecodedRecord(record, fi, contactX, contactY));
                }

                decodedSequences.Add(new DecodedSequence(direction, seqIndex, seq.Terminator, decodedRecords));
            }

            decodedSets.Add(new DecodedSet(group.SetId, PlayerArchiveDecoder.GetSetName(group.SetId), decodedSequences));
        }

        return (decodedSets, footAnchors, groundContacts);
    }

    /// <summary>
    /// Groups each distinct foot-anchor combination <c>(OffsetX, OffsetY, Width, Height)</c> by occurrence
    /// count, producing the <c>anchor_variants</c> list used in the manifest's render-anchor section.
    /// Entries are sorted by offset X, then Y, then width, then height, to match the Python baseline.
    /// </summary>
    /// <param name="footAnchors">Flat list of per-record foot anchors collected during animation decoding.</param>
    /// <returns>Distinct anchor tuples paired with their frequency, sorted for deterministic manifest output.</returns>
    private static List<KeyValuePair<(int Ox, int Oy, int W, int H), int>> BuildAnchorVariants(
        List<(int OffsetX, int OffsetY, int Width, int Height)> footAnchors)
    {
        var anchorCounts = new Dictionary<(int Ox, int Oy, int W, int H), int>();
        foreach (var a in footAnchors)
        {
            var key = (a.OffsetX, a.OffsetY, a.Width, a.Height);
            anchorCounts.TryGetValue(key, out int count);
            anchorCounts[key] = count + 1;
        }

        return [.. anchorCounts
            .OrderBy(kv => kv.Key.Ox).ThenBy(kv => kv.Key.Oy)
            .ThenBy(kv => kv.Key.W).ThenBy(kv => kv.Key.H)];
    }

    /// <summary>
    /// Writes the complete player manifest JSON to <paramref name="w"/>: anchor variants, frame table,
    /// render anchor, animation sets, and source section, all nested under a root <c>player</c> object.
    /// </summary>
    /// <param name="w">JSON writer for the manifest file.</param>
    /// <param name="archive">Archive path, SHA-256, and byte length for the source section.</param>
    /// <param name="frames">Frame info array in frame-index order, used for the frame table section.</param>
    /// <param name="sets">Decoded animation sets, used for the sets section.</param>
    /// <param name="groundContacts">Sorted unique ground-contact positions, used for the render-anchor section.</param>
    /// <param name="anchorVariants">Distinct foot-anchor variants with occurrence counts, used for the anchor-variants section.</param>
    private static void WriteManifest(
        Utf8JsonWriter w,
        PlayerArchiveRef archive,
        FrameInfo[] frames,
        List<DecodedSet> sets,
        List<(double X, double Y)> groundContacts,
        List<KeyValuePair<(int Ox, int Oy, int W, int H), int>> anchorVariants)
    {
        w.WriteStartObject();
        w.WriteStartObject("player");
        WriteAnchorVariants(w, anchorVariants);
        WriteFrameEntries(w, frames);
        WriteRenderAnchor(w, groundContacts);
        WriteSetEntries(w, sets);
        WriteSourceSection(w, archive);
        w.WriteEndObject(); // player
        w.WriteEndObject(); // root
    }

    /// <summary>
    /// Writes the <c>anchor_variants</c> JSON array to <paramref name="w"/>.
    /// Each entry is a two-element array: the anchor tuple <c>[ox, oy, w, h]</c> and its occurrence count.
    /// </summary>
    /// <param name="w">JSON writer positioned inside the <c>player</c> object.</param>
    /// <param name="anchorVariants">Distinct foot-anchor tuples paired with their frequency.</param>
    private static void WriteAnchorVariants(
        Utf8JsonWriter w,
        List<KeyValuePair<(int Ox, int Oy, int W, int H), int>> anchorVariants)
    {
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
    }

    /// <summary>
    /// Writes the <c>frames</c> JSON object to <paramref name="w"/>: keys are decimal frame indices (as strings),
    /// each entry holds <c>size</c>, <c>visible_bbox</c>, and <c>visible_size</c>.
    /// </summary>
    /// <param name="w">JSON writer positioned inside the <c>player</c> object.</param>
    /// <param name="frames">Frame info array in frame-index order.</param>
    private static void WriteFrameEntries(Utf8JsonWriter w, FrameInfo[] frames)
    {
        // frames  (keys are decimal integers as strings, ascending)
        w.WriteStartObject("frames");
        for (int i = 0; i < frames.Length; i++)
        {
            FrameInfo fi = frames[i];
            w.WriteStartObject(i.ToString(CultureInfo.InvariantCulture));
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
    }

    /// <summary>
    /// Writes the <c>render_anchor</c> JSON object to <paramref name="w"/>: unique ground-contact positions,
    /// the FIELDSTG_drawActor draw offset, the TIM size, and an explanatory note about what ground contact means.
    /// </summary>
    /// <param name="w">JSON writer positioned inside the <c>player</c> object.</param>
    /// <param name="groundContacts">Sorted unique ground-contact positions (X as float, Y cast to int).</param>
    private static void WriteRenderAnchor(Utf8JsonWriter w, List<(double X, double Y)> groundContacts)
    {
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
    }

    /// <summary>
    /// Writes the <c>sets</c> JSON array to <paramref name="w"/>: each element has a name, set id,
    /// and a sequences array with per-direction records (frame, duration, offset, size, ground contact).
    /// </summary>
    /// <param name="w">JSON writer positioned inside the <c>player</c> object.</param>
    /// <param name="sets">Decoded animation sets in order of the archive set table.</param>
    private static void WriteSetEntries(Utf8JsonWriter w, List<DecodedSet> sets)
    {
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
    }

    /// <summary>
    /// Writes the <c>source</c> JSON object to <paramref name="w"/>: archive ISO path, SHA-256,
    /// byte length, character and file-entry labels, file number, and pixel-size constants, matching
    /// the Python parity baseline layout.
    /// </summary>
    /// <param name="w">JSON writer positioned inside the <c>player</c> object.</param>
    /// <param name="archive">Archive reference holding the ISO path, SHA-256 hex, and byte length.</param>
    private static void WriteSourceSection(Utf8JsonWriter w, PlayerArchiveRef archive)
    {
        // source — archive metadata written for parity verification
        w.WriteStartObject("source");
        w.WriteNumber("actor_width_pixels", ActorWidthPx);
        w.WriteNumber("archive_entry", PlayerArchiveDecoder.PlayerAnimSetTableEntry);
        w.WriteNumber("bytes", archive.Bytes);
        w.WriteString("character", CharacterLabel);
        w.WriteString("file_entry", FileEntryLabel);
        w.WriteNumber("file_number", FileNumber);
        w.WriteNumber("half_width_pixels", HalfWidthPx);
        w.WriteString("path", archive.IsoPath);
        w.WriteString("sha256", archive.Sha256);
        w.WriteEndObject();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the lowercase hex SHA-256 digest of <paramref name="data"/>.
    /// </summary>
    /// <param name="data">Byte buffer to hash.</param>
    /// <returns>64-character lowercase hex string.</returns>
    private static string ComputeSha256Hex(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Returns the disc file entry whose ISO path matches <paramref name="isoPath"/>.
    /// </summary>
    /// <param name="entries">Flat list of entries from <see cref="Dw3.Disc.DiscSource.ListFiles"/>.</param>
    /// <param name="isoPath">ISO 9660 path to locate (e.g. <c>AAA/DAT/FIELD/SPRT/P002PLAY.BIN</c>).</param>
    /// <returns>The matching entry.</returns>
    /// <exception cref="InvalidDataException">No entry with the given path was found.</exception>
    private static DiscFileEntry RequireFile(IReadOnlyList<DiscFileEntry> entries, string isoPath)
    {
        return entries.SingleOrDefault(e => string.Equals(e.IsoPath, isoPath, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc file missing: {isoPath}");
    }

    // ── Private record types ──────────────────────────────────────────────────

    /// <summary>
    /// Pixel dimensions and visible bounding box for one decoded TIM frame.
    /// </summary>
    private readonly record struct FrameInfo(int Width, int Height, PlayerFrameBbox Bbox);

    /// <summary>
    /// Disc path, SHA-256 hex digest, and byte length of the P002PLAY.BIN archive,
    /// recorded in the manifest source section for parity verification.
    /// </summary>
    private sealed record PlayerArchiveRef(string IsoPath, string Sha256, int Bytes);

    /// <summary>
    /// One decoded animation set: its numeric id, name, and all direction-indexed sequences.
    /// </summary>
    private sealed record DecodedSet(int SetId, string Name, List<DecodedSequence> Sequences);

    /// <summary>
    /// One decoded animation sequence: its direction index (0..N-1), sequence table index, loop/end
    /// terminator, and the list of animation records.
    /// </summary>
    private sealed record DecodedSequence(
        int Direction,
        int Index,
        PlayerAnimSequenceTerminator Terminator,
        List<DecodedRecord> Records);

    /// <summary>
    /// One decoded animation record: the raw source animation record (frame id, duration, draw offset),
    /// the frame's pixel dimensions and bbox, and the computed foot ground-contact position.
    /// </summary>
    private sealed record DecodedRecord(
        PlayerAnimRecord Source,
        FrameInfo Frame,
        double ContactX,
        double ContactY);
}
