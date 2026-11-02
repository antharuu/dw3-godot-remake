using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Dialogue;

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
            DiscFileEntry exeEntry = entries.SingleOrDefault(e => e.IsoPath == ExecutablePath)
                ?? throw new InvalidDataException($"Required disc member missing: {ExecutablePath}");
            var executable = PsxExecutable.Parse(disc.ReadFile(exeEntry));
            var files = new EngineFiles(disc, entries, executable);

            // Read all five source files.
            (_, byte[] sheetArchive) = files.Read(BattleReportDecoder.SheetFileId);
            (_, byte[] textureArchive) = files.Read(BattleReportDecoder.TextureFileId);
            (_, byte[] menuSheet) = files.Read(BattleHudFiles.MenuSheetFileId);
            (_, byte[] menuTexture) = files.Read(BattleHudFiles.MenuTextureFileId);
            DiscFileEntry overlayEntry = entries.SingleOrDefault(e => e.IsoPath == ReportOverlayPath)
                ?? throw new InvalidDataException($"Required disc member missing: {ReportOverlayPath}");
            byte[] overlay = disc.ReadFile(overlayEntry);

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(BattleReportExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
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
    private static void WriteReportDomain(DataPackWriteContext context,
        byte[] sheetArchive, byte[] textureArchive, byte[] menuSheet, byte[] menuTexture, byte[] overlay)
    {
        // STFGTREP sheet archive: first u32 must be 4 (one-entry pointer), then the sheet body.
        if (sheetArchive.Length < 8)
            throw new FormatException("Report sheet archive is too short.");
        uint outerPtr = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(sheetArchive.AsSpan());
        if (outerPtr != 4)
            throw new FormatException($"Report sheet archive outer pointer is {outerPtr}, expected 4.");
        // The sheet body starts at byte 0 (including the 4-byte outer pointer as the archive skip).
        var (reportParts, reportIds, reportOffsets) = HudSpriteSheetParser.Parse(sheetArchive);

        // Verify all required frame ids are present.
        var idSet = new HashSet<int>(reportIds);
        int missingFrameId = BattleReportDecoder.RequiredFrameRows.Keys.FirstOrDefault(id => !idSet.Contains(id), -1);
        if (missingFrameId >= 0)
            throw new FormatException($"STFGTREP sheet lacks required frame id {missingFrameId}.");

        // Build report texture pages (file 0x7A0, model-archive format with 4-byte outer skip).
        IReadOnlyList<HudTexturePage> reportPages = HudTexturePageDecoder.BuildReportTexturePages(textureArchive);

        // Build menu texture pages from CMFONTTM (for the arrow frame).
        IReadOnlyList<HudTexturePage> menuPages = HudTexturePageDecoder.BuildMenuTexturePages(menuTexture);
        var (menuPartsArr, menuIds, menuOffsets) = HudSpriteSheetParser.Parse(menuSheet);
        int arrowPos = BattleReportDecoder.FramePosition(menuIds, BattleReportDecoder.ArrowFrameId);

        // Read and validate the STFGTREP_animations table from the overlay.
        int[,] animations = BattleReportDecoder.ReadAnimations(overlay);

        ReadOnlySpan<byte> reportBody = sheetArchive.AsSpan(4);
        var positions = new Dictionary<int, int>();
        for (int i = 0; i < reportIds.Count; i++) positions[reportIds[i]] = i;

        // Render all STFGTREP frames (variable CLUT row counts per frame).
        var frameRecords = new List<(int Id, (string Path, int W, int H, int Ox, int Oy)[] Rows, (int Part, int Dx, int Dy)[] Parts)>();
        foreach (var (frameId, rowCount) in BattleReportDecoder.RequiredFrameRows)
        {
            int pos = positions[frameId];
            var clutRows = new List<(string Path, int W, int H, int Ox, int Oy)>();
            (int Part, int Dx, int Dy)[]? partsMeta = null;
            for (int row = 0; row < rowCount; row++)
            {
                var (rgba, w, h, ox, oy, _, meta) = HudFrameRenderer.RenderFrameWithMeta(
                    reportParts, reportBody, reportOffsets[pos], reportPages, row);
                if (row == 0) partsMeta = [.. meta];
                string name = $"frame-{frameId:X2}-clut{row}.png";
                string rel = $"{OutputSubdir}/{name}";
                File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(w, h, rgba));
                context.RegisterImage(rel);
                clutRows.Add((name, w, h, ox, oy));
            }
            frameRecords.Add((frameId, [.. clutRows], partsMeta!));
        }

        // Render the arrow frame (MESSAGE_ARROW_FRAME = 10) from the menu sheet, 5 CLUT rows.
        var arrowRows = new List<(string Path, int W, int H, int Ox, int Oy, IReadOnlyList<FieldFramePartMeta> Parts)>();
        ReadOnlySpan<byte> menuBody = menuSheet.AsSpan(4);
        IReadOnlyList<FieldFramePartMeta>? arrowPartsMeta = null;
        for (int row = 0; row < BattleReportDecoder.ArrowRows; row++)
        {
            var (rgba, w, h, meta, _) = HudFrameRenderer.RenderFieldFrame(menuPartsArr, menuBody, menuOffsets[arrowPos], menuPages, row);
            if (row == 0) arrowPartsMeta = meta;
            string name = $"arrow-clut{row}.png";
            string rel = $"{OutputSubdir}/{name}";
            File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(w, h, rgba));
            context.RegisterImage(rel);
            int ox = meta.Min(p => p.Dx);
            int oy = meta.Min(p => p.Dy);
            arrowRows.Add((name, w, h, ox, oy, meta));
        }

        string reportJson = BattleReportJsonWriter.Build(frameRecords, arrowRows, arrowPartsMeta!, animations, sheetArchive, textureArchive, overlay);
        File.WriteAllText(context.GetImagePath($"{OutputSubdir}/report.json"), reportJson, new System.Text.UTF8Encoding(false));
        WriteMenuIcons(context, menuPartsArr, menuSheet, menuIds, menuOffsets, menuPages);
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
    private static void WriteMenuIcons(DataPackWriteContext context, IReadOnlyList<HudSpritePart> parts, byte[] sheet,
        IReadOnlyList<int> ids, IReadOnlyList<int> offsets, IReadOnlyList<HudTexturePage> pages)
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
                width = w; height = h; ox = meta.Min(part => part.Dx); oy = meta.Min(part => part.Dy);
                string relative = $"{directory}/frame-{id}-clut{row}.png";
                File.WriteAllBytes(context.GetImagePath(relative), PngEncoder.Encode(w, h, rgba));
                context.RegisterImage(relative); paths[row] = relative;
            }
            frames.Add(id, (paths, width, height, ox, oy));
        }
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject(); json.WriteString("schema", "m6-battle-menu-icons-v1"); json.WriteStartObject("frames");
            foreach (var (id, frame) in frames)
            {
                json.WriteStartObject(id.ToString(CultureInfo.InvariantCulture)); json.WriteNumber("w", frame.W); json.WriteNumber("h", frame.H);
                json.WriteStartArray("offset"); json.WriteNumberValue(frame.Ox); json.WriteNumberValue(frame.Oy); json.WriteEndArray();
                json.WriteNumber("clutFrames", frame.Paths.Length);
                json.WriteStartArray("paths");
                foreach (string path in frame.Paths)
                {
                    json.WriteStringValue(path);
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndObject(); json.WriteStartObject("messageArrow"); json.WriteNumber("frame", 10);
            json.WriteStartArray("position"); json.WriteNumberValue(291); json.WriteNumberValue(208); json.WriteEndArray();
            json.WriteNumber("timeStep", 4); json.WriteEndObject(); json.WriteEndObject();
        }
        File.WriteAllBytes(context.GetImagePath($"{directory}/manifest.json"), stream.ToArray());
    }

    /// <summary>
    /// Reads disc members by engine file id through the executable's file-sector table.
    /// Zero-length disc entries are excluded from the LBA-to-path map.
    /// </summary>
    private sealed class EngineFiles
    {
        private readonly DiscSource _disc;
        private readonly DiscFileTable _table;
        private readonly Dictionary<string, DiscFileEntry> _byPath;

        /// <summary>
        /// Initialises the file-sector table and ISO path index from the executable and disc entries.
        /// </summary>
        /// <param name="disc">The open disc source for subsequent file reads.</param>
        /// <param name="entries">All disc file entries; zero-length entries are excluded from the LBA map.</param>
        /// <param name="executable">The parsed PS1 executable carrying the FILE_SECTORS table.</param>
        internal EngineFiles(DiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable)
        {
            _disc = disc;
            DialogueRomLayout layout = DialogueRomLayout.Sles03936;
            IReadOnlyDictionary<int, string> pathByLba = entries
                .Where(e => e.Size > 0).GroupBy(e => e.Lba).ToDictionary(g => g.Key, g => g.First().IsoPath);
            _table = DiscFileTable.Read(executable, layout.FileSectorsAddress, layout.FileSectorCount, pathByLba);
            _byPath = entries.Where(e => e.Size > 0).GroupBy(e => e.IsoPath).ToDictionary(g => g.Key, g => g.First());
        }

        /// <summary>
        /// Returns the ISO path and raw bytes for the disc member at engine file id <paramref name="fileId"/>.
        /// </summary>
        /// <param name="fileId">The engine file id (0-based index into the FILE_SECTORS table).</param>
        /// <returns>The ISO path and raw byte content of the disc member.</returns>
        /// <exception cref="FormatException">The file id is outside the table or no disc member starts at its sector.</exception>
        internal (string Path, byte[] Bytes) Read(int fileId)
        {
            string path = _table.PathOf(fileId)
                ?? throw new FormatException($"Engine file 0x{fileId:X} does not start a disc member.");
            return (path, _disc.ReadFile(_byPath[path]));
        }
    }
}
