using System.Security.Cryptography;
using System.Text;
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
            (_, byte[] menuSheet) = files.Read(BattleHudDecoder.MenuSheetFileId);
            (_, byte[] menuTexture) = files.Read(BattleHudDecoder.MenuTextureFileId);
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
        var (reportParts, reportIds, reportOffsets) = BattleHudDecoder.ParseSheet(sheetArchive);

        // Verify all required frame ids are present.
        var idSet = new HashSet<int>(reportIds);
        foreach (int required in BattleReportDecoder.RequiredFrameRows.Keys)
            if (!idSet.Contains(required))
                throw new FormatException($"STFGTREP sheet lacks required frame id {required}.");

        // Build report texture pages (file 0x7A0, model-archive format with 4-byte outer skip).
        IReadOnlyList<HudTexturePage> reportPages = BattleHudDecoder.BuildReportTexturePages(textureArchive);

        // Build menu texture pages from CMFONTTM (for the arrow frame).
        IReadOnlyList<HudTexturePage> menuPages = BattleHudDecoder.BuildMenuTexturePages(menuTexture);
        var (menuPartsArr, menuIds, menuOffsets) = BattleHudDecoder.ParseSheet(menuSheet);
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
                var (rgba, w, h, ox, oy, _, meta) = BattleHudDecoder.RenderFrameWithMeta(
                    reportParts, reportBody, reportOffsets[pos], reportPages, row);
                if (row == 0) partsMeta = meta.ToArray();
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
            var (rgba, w, h, meta, _) = BattleHudDecoder.RenderFieldFrame(menuPartsArr, menuBody, menuOffsets[arrowPos], menuPages, row);
            if (row == 0) arrowPartsMeta = meta;
            string name = $"arrow-clut{row}.png";
            string rel = $"{OutputSubdir}/{name}";
            File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(w, h, rgba));
            context.RegisterImage(rel);
            int ox = meta.Min(p => p.Dx);
            int oy = meta.Min(p => p.Dy);
            arrowRows.Add((name, w, h, ox, oy, meta));
        }

        string reportJson = BuildReportJson(frameRecords, arrowRows, arrowPartsMeta!, animations, sheetArchive, textureArchive, overlay);
        File.WriteAllText(context.GetImagePath($"{OutputSubdir}/report.json"), reportJson, new System.Text.UTF8Encoding(false));
        WriteMenuIcons(context, menuPartsArr, menuSheet, menuIds, menuOffsets, menuPages);
    }

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
                var (rgba, w, h, meta, _) = BattleHudDecoder.RenderFieldFrame(parts, body, offsets[position], pages, row);
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
                json.WriteStartObject(id.ToString()); json.WriteNumber("w", frame.W); json.WriteNumber("h", frame.H);
                json.WriteStartArray("offset"); json.WriteNumberValue(frame.Ox); json.WriteNumberValue(frame.Oy); json.WriteEndArray();
                json.WriteNumber("clutFrames", frame.Paths.Length); json.WriteStartArray("paths");
                foreach (string path in frame.Paths) json.WriteStringValue(path); json.WriteEndArray(); json.WriteEndObject();
            }
            json.WriteEndObject(); json.WriteStartObject("messageArrow"); json.WriteNumber("frame", 10);
            json.WriteStartArray("position"); json.WriteNumberValue(291); json.WriteNumberValue(208); json.WriteEndArray();
            json.WriteNumber("timeStep", 4); json.WriteEndObject(); json.WriteEndObject();
        }
        File.WriteAllBytes(context.GetImagePath($"{directory}/manifest.json"), stream.ToArray());
    }

    private static string BuildReportJson(
        List<(int Id, (string Path, int W, int H, int Ox, int Oy)[] Rows, (int Part, int Dx, int Dy)[] Parts)> frames,
        List<(string Path, int W, int H, int Ox, int Oy, IReadOnlyList<FieldFramePartMeta> Parts)> arrowRows,
        IReadOnlyList<FieldFramePartMeta> arrowPartsMeta,
        int[,] animations, byte[] sheetArchive, byte[] textureArchive, byte[] overlay)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("schema", "m6-battle-report-sprites-v1");

            // source
            json.WriteStartObject("source");
            json.WriteStartObject("sheet");
            json.WriteNumber("file_id", BattleReportDecoder.SheetFileId);
            json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(sheetArchive)));
            json.WriteNumber("bytes", sheetArchive.Length);
            json.WriteNumber("entry", 0);
            json.WriteEndObject();
            json.WriteStartObject("texture");
            json.WriteNumber("file_id", BattleReportDecoder.TextureFileId);
            json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(textureArchive)));
            json.WriteNumber("bytes", textureArchive.Length);
            json.WriteStartArray("vram"); json.WriteNumberValue(0x280); json.WriteNumberValue(0); json.WriteEndArray();
            json.WriteEndObject();
            json.WriteStartObject("overlay");
            json.WriteString("path", BattleReportDecoder.OverlayDiscPath);
            json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(overlay)));
            json.WriteNumber("bytes", overlay.Length);
            json.WriteNumber("base", (long)BattleReportDecoder.OverlayBase);
            json.WriteNumber("animations_addr", (long)BattleReportDecoder.AnimationsAddr);
            json.WriteEndObject();
            json.WriteStartObject("arrow");
            json.WriteNumber("frame", BattleReportDecoder.ArrowFrameId);
            json.WriteStartArray("vram"); json.WriteNumberValue(0x140); json.WriteNumberValue(0); json.WriteEndArray();
            json.WriteNumber("clutRows", BattleReportDecoder.ArrowRows);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteNumber("frameCount", frames.Count);
            json.WriteStartArray("frameIds");
            foreach (var (id, _, _) in frames) json.WriteNumberValue(id);
            json.WriteEndArray();

            json.WriteStartObject("frames");
            foreach (var (frameId, rows, parts) in frames)
            {
                json.WriteStartObject(frameId.ToString());
                json.WriteString("path", rows[0].Path);
                json.WriteNumber("w", rows[0].W);
                json.WriteNumber("h", rows[0].H);
                json.WriteNumber("offsetX", rows[0].Ox);
                json.WriteNumber("offsetY", rows[0].Oy);
                json.WriteStartArray("parts");
                foreach (var (partIdx, dx, dy) in parts)
                {
                    json.WriteStartObject();
                    json.WriteNumber("part", partIdx);
                    json.WriteNumber("dx", dx);
                    json.WriteNumber("dy", dy);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("clutRows");
                foreach (var (path, w, h, ox, oy) in rows)
                {
                    json.WriteStartObject();
                    json.WriteString("path", path);
                    json.WriteNumber("w", w);
                    json.WriteNumber("h", h);
                    json.WriteNumber("offsetX", ox);
                    json.WriteNumber("offsetY", oy);
                    json.WriteStartArray("parts");
                    foreach (var (partIdx, dx, dy) in parts)
                    {
                        json.WriteStartObject();
                        json.WriteNumber("part", partIdx);
                        json.WriteNumber("dx", dx);
                        json.WriteNumber("dy", dy);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndObject();

            // Animations: 8 rows × 7 values.
            json.WriteStartArray("animations");
            for (int p = 0; p < BattleReportDecoder.AnimationPartners; p++)
            {
                json.WriteStartArray();
                for (int f = 0; f < BattleReportDecoder.AnimationFrames; f++) json.WriteNumberValue(animations[p, f]);
                json.WriteEndArray();
            }
            json.WriteEndArray();

            // Arrow.
            var firstArrow = arrowRows[0];
            json.WriteStartObject("arrow");
            json.WriteNumber("frameId", BattleReportDecoder.ArrowFrameId);
            json.WriteString("path", firstArrow.Path);
            json.WriteNumber("w", firstArrow.W);
            json.WriteNumber("h", firstArrow.H);
            json.WriteNumber("offsetX", firstArrow.Ox);
            json.WriteNumber("offsetY", firstArrow.Oy);
            json.WriteStartArray("parts");
            foreach (FieldFramePartMeta m in arrowPartsMeta)
            {
                json.WriteStartObject();
                json.WriteNumber("u", m.U); json.WriteNumber("v", m.V); json.WriteNumber("w", m.W); json.WriteNumber("h", m.H);
                json.WriteNumber("dx", m.Dx); json.WriteNumber("dy", m.Dy);
                json.WriteBoolean("semi", m.Semi);
                json.WriteNumber("clutRow", m.ClutRow);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("clutRows");
            foreach (var (path, w, h, ox, oy, parts) in arrowRows)
            {
                json.WriteStartObject();
                json.WriteString("path", path); json.WriteNumber("w", w); json.WriteNumber("h", h);
                json.WriteNumber("offsetX", ox); json.WriteNumber("offsetY", oy);
                json.WriteStartArray("parts");
                foreach (FieldFramePartMeta m in parts)
                {
                    json.WriteStartObject();
                    json.WriteNumber("u", m.U); json.WriteNumber("v", m.V); json.WriteNumber("w", m.W); json.WriteNumber("h", m.H);
                    json.WriteNumber("dx", m.Dx); json.WriteNumber("dy", m.Dy);
                    json.WriteBoolean("semi", m.Semi);
                    json.WriteNumber("clutRow", m.ClutRow);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private sealed class EngineFiles
    {
        private readonly DiscSource _disc;
        private readonly DiscFileTable _table;
        private readonly IReadOnlyDictionary<string, DiscFileEntry> _byPath;

        internal EngineFiles(DiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable)
        {
            _disc = disc;
            DialogueRomLayout layout = DialogueRomLayout.Sles03936;
            IReadOnlyDictionary<int, string> pathByLba = entries
                .Where(e => e.Size > 0).GroupBy(e => e.Lba).ToDictionary(g => g.Key, g => g.First().IsoPath);
            _table = DiscFileTable.Read(executable, layout.FileSectorsAddress, layout.FileSectorCount, pathByLba);
            _byPath = entries.Where(e => e.Size > 0).GroupBy(e => e.IsoPath).ToDictionary(g => g.Key, g => g.First());
        }

        internal (string Path, byte[] Bytes) Read(int fileId)
        {
            string path = _table.PathOf(fileId)
                ?? throw new FormatException($"Engine file 0x{fileId:X} does not start a disc member.");
            return (path, _disc.ReadFile(_byPath[path]));
        }
    }
}
