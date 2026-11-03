using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Binary.Png;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the STFGTREP result-screen sprite assets.
/// Sources: file 0x79F (sheet archive), file 0x7A0 (texture archive), CMFONTCS (0x286, arrow
/// sheet), CMFONTTM (0x287, arrow texture), STFGTREP.PRO overlay (animation table).
/// Writes report.json + PNG frames under battle/report/ in the data pack.
/// </summary>
internal static class BattleReportExtractionStep
{
    private const string Domain = "battle-report";
    private const string ExecutablePath = "SLES_039.36";
    private const string ReportOverlayPath = "AAA/PRO/STFGTREP.PRO";
    private const string OutputSubdir = "battle/report";

    /// <summary>
    /// Reads file 0x79F (sheet), file 0x7A0 (texture), CMFONTCS (0x286, arrow sheet), CMFONTTM (0x287,
    /// arrow texture) and STFGTREP.PRO from the disc, then renders all result-screen frames and writes
    /// the data pack.
    /// </summary>
    /// <param name="openDisc">Opens the disc whose executable file table locates the report screen files; disposed before returning.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on extraction error.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscFileEntry exeEntry = entries.SingleOrDefault(e => e.IsoPath == ExecutablePath)
                ?? throw new InvalidDataException($"Required disc member missing: {ExecutablePath}");
            var executable = PsxExecutable.Parse(disc.ReadFile(exeEntry));
            var files = new EngineFileReader(disc, entries, executable);

            // Read all five source files.
            (_, byte[] sheetArchive) = files.Read(BattleReportDecoder.SheetFileId);
            (_, byte[] textureArchive) = files.Read(BattleReportDecoder.TextureFileId);
            (_, byte[] menuSheet) = files.Read(BattleHudFiles.MenuSheetFileId);
            (_, byte[] menuTexture) = files.Read(BattleHudFiles.MenuTextureFileId);
            DiscFileEntry overlayEntry = entries.SingleOrDefault(e => e.IsoPath == ReportOverlayPath)
                ?? throw new InvalidDataException($"Required disc member missing: {ReportOverlayPath}");
            byte[] overlay = disc.ReadFile(overlayEntry);

            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(
                typeof(BattleReportExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow),
                context =>
                {
                    Directory.CreateDirectory(context.GetImagePath(OutputSubdir));
                    WriteReportDomain(context, sheetArchive, textureArchive, menuSheet, menuTexture, overlay);
                });

            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\"}}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Decodes and validates all STFGTREP sheet frames and the menu arrow frame, renders every
    /// required frame with variable CLUT row counts, and writes PNGs and <c>report.json</c>.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="sheetArchive">The STFGTREP sheet archive (file 0x79F); outer uint32 must equal 4.</param>
    /// <param name="textureArchive">The STFGTREP texture archive (file 0x7A0).</param>
    /// <param name="menuSheet">The CMFONTCS sheet archive (file 0x286), carrying the arrow frame.</param>
    /// <param name="menuTexture">The CMFONTTM texture archive (file 0x287), carrying the arrow palette.</param>
    /// <param name="overlay">The STFGTREP.PRO overlay bytes containing the animation table.</param>
    private static void WriteReportDomain(
        DataPackWriteContext context,
        byte[] sheetArchive,
        byte[] textureArchive,
        byte[] menuSheet,
        byte[] menuTexture,
        byte[] overlay)
    {
        var (reportParts, reportIds, reportOffsets) = ParseReportSheet(sheetArchive);

        // Build report texture pages (file 0x7A0, model-archive format with 4-byte outer skip).
        IReadOnlyList<HudTexturePage> reportPages = HudTexturePageDecoder.BuildReportTexturePages(textureArchive);

        // Build menu texture pages from CMFONTTM (for the arrow frame).
        IReadOnlyList<HudTexturePage> menuPages = HudTexturePageDecoder.BuildMenuTexturePages(menuTexture);
        var (menuPartsArr, menuIds, menuOffsets) = HudSpriteSheetParser.Parse(menuSheet);
        int arrowPos = BattleReportDecoder.FramePosition(menuIds, BattleReportDecoder.ArrowFrameId);

        // Read and validate the STFGTREP_animations table from the overlay.
        int[,] animations = BattleReportDecoder.ReadAnimations(overlay);

        var frameRecords = WriteReportFrames(context, reportParts, sheetArchive, reportIds, reportOffsets, reportPages);
        var (arrowRows, arrowPartsMeta) = WriteArrowFrames(context, menuPartsArr, menuSheet, menuOffsets[arrowPos], menuPages);

        string reportJson = BattleReportJsonWriter.Build(frameRecords, arrowRows, arrowPartsMeta, animations, sheetArchive, textureArchive, overlay);
        File.WriteAllText(context.GetImagePath($"{OutputSubdir}/report.json"), reportJson, new System.Text.UTF8Encoding(false));
        WriteMenuIcons(context, menuPartsArr, menuSheet, menuIds, menuOffsets, menuPages);
    }

    /// <summary>
    /// Checks the STFGTREP sheet archive header and parses its sprite sheet, so that every frame the
    /// report screen draws is known to exist before anything is rendered.
    /// </summary>
    /// <param name="sheetArchive">The STFGTREP sheet archive: a one-entry pointer table whose first u32 is 4, then the sheet body.</param>
    /// <returns>The sprite parts, the frame ids in sheet order and the per-frame offsets into the sheet body.</returns>
    /// <exception cref="FormatException">The archive is shorter than 8 bytes, its outer pointer is not 4, or a required frame id is missing.</exception>
    private static (IReadOnlyList<HudSpritePart> Parts, IReadOnlyList<int> Ids, IReadOnlyList<int> Offsets) ParseReportSheet(byte[] sheetArchive)
    {
        if (sheetArchive.Length < 8)
        {
            throw new FormatException("Report sheet archive is too short.");
        }

        uint outerPtr = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sheetArchive.AsSpan());
        if (outerPtr != 4)
        {
            throw new FormatException($"Report sheet archive outer pointer is {outerPtr}, expected 4.");
        }

        // The sheet body starts at byte 0 (including the 4-byte outer pointer as the archive skip).
        var (reportParts, reportIds, reportOffsets) = HudSpriteSheetParser.Parse(sheetArchive);

        var idSet = new HashSet<int>(reportIds);
        int missingFrameId = BattleReportDecoder.RequiredFrameRows.Keys.FirstOrDefault(id => !idSet.Contains(id), -1);
        if (missingFrameId >= 0)
        {
            throw new FormatException($"STFGTREP sheet lacks required frame id {missingFrameId}.");
        }

        return (reportParts, reportIds, reportOffsets);
    }

    /// <summary>
    /// Renders every required STFGTREP frame once per CLUT row (the row count varies per frame) and
    /// writes each rendering as <c>frame-XX-clutN.png</c> under the battle-report image folder.
    /// </summary>
    /// <param name="context">The data pack write context that owns the image folder.</param>
    /// <param name="parts">The sprite parts of the STFGTREP sheet.</param>
    /// <param name="sheetArchive">The STFGTREP sheet archive; frame offsets are relative to byte 4.</param>
    /// <param name="ids">The frame ids in sheet order.</param>
    /// <param name="offsets">The per-frame offsets into the sheet body, parallel to <paramref name="ids"/>.</param>
    /// <param name="pages">The STFGTREP texture pages.</param>
    /// <returns>One record per required frame: its id, the written CLUT rows and the part layout of row 0.</returns>
    private static List<(int Id, (string Path, int W, int H, int Ox, int Oy)[] Rows, (int Part, int Dx, int Dy)[] Parts)> WriteReportFrames(
        DataPackWriteContext context,
        IReadOnlyList<HudSpritePart> parts,
        byte[] sheetArchive,
        IReadOnlyList<int> ids,
        IReadOnlyList<int> offsets,
        IReadOnlyList<HudTexturePage> pages)
    {
        ReadOnlySpan<byte> reportBody = sheetArchive.AsSpan(4);
        var positions = new Dictionary<int, int>();
        for (int i = 0; i < ids.Count; i++)
        {
            positions[ids[i]] = i;
        }

        var frameRecords = new List<(int Id, (string Path, int W, int H, int Ox, int Oy)[] Rows, (int Part, int Dx, int Dy)[] Parts)>();
        foreach (var (frameId, rowCount) in BattleReportDecoder.RequiredFrameRows)
        {
            int pos = positions[frameId];
            var clutRows = new List<(string Path, int W, int H, int Ox, int Oy)>();
            (int Part, int Dx, int Dy)[] partsMeta = [];
            for (int row = 0; row < rowCount; row++)
            {
                var (rgba, w, h, ox, oy, _, meta) = HudFrameRenderer.RenderFrameWithMeta(
                    parts, reportBody, offsets[pos], pages, row);
                if (row == 0)
                {
                    partsMeta = [.. meta];
                }

                string name = $"frame-{frameId:X2}-clut{row}.png";
                string rel = $"{OutputSubdir}/{name}";
                File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(w, h, rgba));
                context.RegisterImage(rel);
                clutRows.Add((name, w, h, ox, oy));
            }

            frameRecords.Add((frameId, [.. clutRows], partsMeta));
        }

        return frameRecords;
    }

    /// <summary>
    /// Renders the message arrow (frame 10 of the CMFONTCS menu sheet) once per palette row and writes
    /// each rendering as <c>arrow-clutN.png</c>; the five rows are the colours the arrow blinks through.
    /// </summary>
    /// <param name="context">The data pack write context that owns the image folder.</param>
    /// <param name="parts">The sprite parts of the CMFONTCS sheet.</param>
    /// <param name="menuSheet">The CMFONTCS sheet archive; frame offsets are relative to byte 4.</param>
    /// <param name="arrowOffset">The offset of the arrow frame in the sheet body.</param>
    /// <param name="pages">The CMFONTTM texture pages.</param>
    /// <returns>The written rows with their size, origin and parts, and the part layout of row 0.</returns>
    private static (List<(string Path, int W, int H, int Ox, int Oy, IReadOnlyList<FieldFramePartMeta> Parts)> Rows, IReadOnlyList<FieldFramePartMeta> Parts) WriteArrowFrames(
        DataPackWriteContext context,
        IReadOnlyList<HudSpritePart> parts,
        byte[] menuSheet,
        int arrowOffset,
        IReadOnlyList<HudTexturePage> pages)
    {
        var arrowRows = new List<(string Path, int W, int H, int Ox, int Oy, IReadOnlyList<FieldFramePartMeta> Parts)>();
        ReadOnlySpan<byte> menuBody = menuSheet.AsSpan(4);
        IReadOnlyList<FieldFramePartMeta> arrowPartsMeta = [];
        for (int row = 0; row < BattleReportDecoder.ArrowRows; row++)
        {
            var (rgba, w, h, meta, _) = HudFrameRenderer.RenderFieldFrame(parts, menuBody, arrowOffset, pages, row);
            if (row == 0)
            {
                arrowPartsMeta = meta;
            }

            string name = $"arrow-clut{row}.png";
            string rel = $"{OutputSubdir}/{name}";
            File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(w, h, rgba));
            context.RegisterImage(rel);
            int ox = meta.Min(p => p.Dx);
            int oy = meta.Min(p => p.Dy);
            arrowRows.Add((name, w, h, ox, oy, meta));
        }

        return (arrowRows, arrowPartsMeta);
    }

    /// <summary>
    /// Renders and writes the eight required menu icon frames (including frame 10 with 5 CLUT rows)
    /// from the CMFONTCS sprite sheet and CMFONTTM texture, then writes <c>battle/menu-icons/manifest.json</c>.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="parts">The sprite-part list from the CMFONTCS sheet header.</param>
    /// <param name="sheet">The raw bytes of the CMFONTCS sprite sheet archive.</param>
    /// <param name="ids">The ordered frame id list from the CMFONTCS sheet.</param>
    /// <param name="offsets">Per-frame byte offsets within the sheet body.</param>
    /// <param name="pages">The menu texture pages decoded from CMFONTTM.</param>
    private static void WriteMenuIcons(
        DataPackWriteContext context,
        IReadOnlyList<HudSpritePart> parts,
        byte[] sheet,
        IReadOnlyList<int> ids,
        IReadOnlyList<int> offsets,
        IReadOnlyList<HudTexturePage> pages)
    {
        const string directory = "battle/menu-icons";
        int[] required = [10, 57, 58, 59, 60, 61, 95, 97];
        Directory.CreateDirectory(context.GetImagePath(directory));
        var frames = new Dictionary<int, (string[] Paths, int W, int H, int Ox, int Oy)>();
        ReadOnlySpan<byte> body = sheet.AsSpan(4);
        foreach (int id in required)
        {
            int position = BattleReportDecoder.FramePosition(ids, id);
            int rowCount = id == 10 ? 5 : 1;
            var paths = new string[rowCount];
            int width = 0, height = 0, ox = 0, oy = 0;
            for (int row = 0; row < rowCount; row++)
            {
                var (rgba, w, h, meta, _) = HudFrameRenderer.RenderFieldFrame(parts, body, offsets[position], pages, row);
                width = w;
                height = h;
                ox = meta.Min(part => part.Dx);
                oy = meta.Min(part => part.Dy);
                string relative = $"{directory}/frame-{id}-clut{row}.png";
                File.WriteAllBytes(context.GetImagePath(relative), PngEncoder.Encode(w, h, rgba));
                context.RegisterImage(relative);
                paths[row] = relative;
            }

            frames.Add(id, (paths, width, height, ox, oy));
        }

        File.WriteAllBytes(context.GetImagePath($"{directory}/manifest.json"), BuildMenuIconManifest(frames));
    }

    /// <summary>
    /// Serialises the menu icon manifest read by the battle menu: per frame its size, origin offset and
    /// the PNG of each CLUT row, plus the message arrow placement (frame 10 drawn at screen pixel
    /// (291, 208), advancing one palette row every 4 frames).
    /// </summary>
    /// <param name="frames">The written icon frames keyed by sheet frame id, in the order they were rendered.</param>
    /// <returns>The UTF-8 JSON bytes of <c>manifest.json</c>, schema <c>m6-battle-menu-icons-v1</c>.</returns>
    private static byte[] BuildMenuIconManifest(Dictionary<int, (string[] Paths, int W, int H, int Ox, int Oy)> frames)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("schema", "m6-battle-menu-icons-v1");
            json.WriteStartObject("frames");
            foreach (var (id, frame) in frames)
            {
                json.WriteStartObject(id.ToString(CultureInfo.InvariantCulture));
                json.WriteNumber("w", frame.W);
                json.WriteNumber("h", frame.H);
                json.WriteStartArray("offset");
                json.WriteNumberValue(frame.Ox);
                json.WriteNumberValue(frame.Oy);
                json.WriteEndArray();
                json.WriteNumber("clutFrames", frame.Paths.Length);
                json.WriteStartArray("paths");
                foreach (string path in frame.Paths)
                {
                    json.WriteStringValue(path);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteStartObject("messageArrow");
            json.WriteNumber("frame", 10);
            json.WriteStartArray("position");
            json.WriteNumberValue(291);
            json.WriteNumberValue(208);
            json.WriteEndArray();
            json.WriteNumber("timeStep", 4);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return stream.ToArray();
    }
}
