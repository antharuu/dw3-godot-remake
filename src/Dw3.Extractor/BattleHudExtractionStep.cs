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
/// Extracts the battle HUD sprite sheet (WFIGHTMN, file 0x455) and its texture (file 0x456).
/// Decodes all HUD frames (5 CLUT rows each) and the cursor bar strip into PNGs,
/// and writes hud.json under battle/hud/ in the data pack.
/// </summary>
internal static class BattleHudExtractionStep
{
    private const string Domain = "battle-hud";
    private const string ExecutablePath = "SLES_039.36";
    private const string OutputSubdir = "battle/hud";
    private const string Schema = "m6-battle-hud-v3";
    private const int ClutRowsPerFrame = 5;

    // Required HUD frame ids (HP bar caps, background, tech gauge blink frames 0x33..0x3A).
    private static readonly int[] RequiredFrameIds = [0, 1, 0xB, 0x1E, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A];

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

            (_, byte[] sheetData) = files.Read(BattleHudFiles.HudSheetFileId);
            (_, byte[] imagesData) = files.Read(BattleHudFiles.HudImagesFileId);

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(BattleHudExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
                context =>
                {
                    Directory.CreateDirectory(context.GetImagePath(OutputSubdir));
                    WriteHudDomain(context, sheetData, imagesData);
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

    private static void WriteHudDomain(DataPackWriteContext context, byte[] sheetData, byte[] imagesData)
    {
        var (parts, ids, frameOffsets) = HudSpriteSheetParser.Parse(sheetData);
        IReadOnlyList<HudTexturePage> pages = HudTexturePageDecoder.BuildHudTexturePages(imagesData);
        ReadOnlySpan<byte> sheetBody = sheetData.AsSpan(4);

        var idSet = new HashSet<int>(ids);
        foreach (int required in RequiredFrameIds)
            if (!idSet.Contains(required))
                throw new FormatException($"Battle HUD sheet lacks required frame id {required}.");
        if (idSet.Count != ids.Count)
            throw new FormatException("Battle HUD sprite sheet contains duplicate frame ids.");

        // Render all frames with 5 CLUT rows each.
        var frameRecords = new List<(int Id, List<(string Path, int W, int H, int Ox, int Oy)> Rows)>();
        for (int pos = 0; pos < ids.Count; pos++)
        {
            int frameId = ids[pos];
            var clutRows = new List<(string Path, int W, int H, int Ox, int Oy)>();
            for (int row = 0; row < ClutRowsPerFrame; row++)
            {
                (byte[] rgba, int w, int h, int ox, int oy, _) = TryRenderFrame(parts, sheetBody, frameOffsets[pos], pages, row);
                string name = $"frame-{frameId}-clut{row}.png";
                string rel = $"{OutputSubdir}/{name}";
                File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(w, h, rgba));
                context.RegisterImage(rel);
                clutRows.Add((name, w, h, ox, oy));
            }
            frameRecords.Add((frameId, clutRows));
        }

        // Cursor bar.
        var (cw, ch, cursorRgba) = HudCursorBarDecoder.Decode(imagesData);
        File.WriteAllBytes(context.GetImagePath($"{OutputSubdir}/cursor.png"), PngEncoder.Encode(cw, ch, cursorRgba));
        context.RegisterImage($"{OutputSubdir}/cursor.png");
        byte[] offRgba = CropCursorFrame(cursorRgba, cw, HudCursorBarDecoder.CursorFrames, HudCursorBarDecoder.CursorW, HudCursorBarDecoder.CursorH);
        File.WriteAllBytes(context.GetImagePath($"{OutputSubdir}/cursor-off.png"), PngEncoder.Encode(HudCursorBarDecoder.CursorW, HudCursorBarDecoder.CursorH, offRgba));
        context.RegisterImage($"{OutputSubdir}/cursor-off.png");

        // Write hud.json using Utf8JsonWriter for exact property names.
        string hudJson = BuildHudJson(frameRecords, ids, sheetData, imagesData, cw, ch);
        File.WriteAllText(context.GetImagePath($"{OutputSubdir}/hud.json"), hudJson, new System.Text.UTF8Encoding(false));
    }

    private static string BuildHudJson(
        List<(int Id, List<(string Path, int W, int H, int Ox, int Oy)> Rows)> frameRecords,
        IReadOnlyList<int> ids, byte[] sheetData, byte[] imagesData, int cw, int ch)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("schema", Schema);

            json.WriteStartObject("source");
            json.WriteStartObject("sheet");
            json.WriteNumber("file_id", BattleHudFiles.HudSheetFileId);
            json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(sheetData)));
            json.WriteNumber("bytes", sheetData.Length);
            json.WriteEndObject();
            json.WriteStartObject("images");
            json.WriteNumber("file_id", BattleHudFiles.HudImagesFileId);
            json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(imagesData)));
            json.WriteNumber("bytes", imagesData.Length);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteNumber("frameCount", ids.Count);
            json.WriteStartArray("frameIds");
            foreach (int id in ids) json.WriteNumberValue(id);
            json.WriteEndArray();

            json.WriteStartObject("frames");
            foreach (var (frameId, rows) in frameRecords)
            {
                json.WriteStartObject(frameId.ToString());
                json.WriteString("path", rows[0].Path);
                json.WriteNumber("w", rows[0].W);
                json.WriteNumber("h", rows[0].H);
                json.WriteNumber("offsetX", rows[0].Ox);
                json.WriteNumber("offsetY", rows[0].Oy);
                json.WriteStartArray("clutRows");
                foreach (var (path, w, h, ox, oy) in rows)
                {
                    json.WriteStartObject();
                    json.WriteString("path", path);
                    json.WriteNumber("w", w);
                    json.WriteNumber("h", h);
                    json.WriteNumber("offsetX", ox);
                    json.WriteNumber("offsetY", oy);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndObject();

            // Cursor.
            json.WriteStartObject("cursor");
            json.WriteString("path", "cursor.png");
            json.WriteNumber("w", cw);
            json.WriteNumber("h", ch);
            json.WriteNumber("frames", HudCursorBarDecoder.CursorFrames);
            json.WriteNumber("frameW", HudCursorBarDecoder.CursorW);
            json.WriteNumber("frameH", HudCursorBarDecoder.CursorH);
            json.WriteString("offPath", "cursor-off.png");
            json.WriteEndObject();

            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    // Falls back to row 0 when the computed CLUT row is outside the page (Python behaviour).
    private static (byte[] Rgba, int W, int H, int Ox, int Oy, int Abr) TryRenderFrame(
        IReadOnlyList<HudSpritePart> parts, ReadOnlySpan<byte> sheetBody, int frameOffset,
        IReadOnlyList<HudTexturePage> pages, int row)
    {
        try { return HudFrameRenderer.RenderFrame(parts, sheetBody, frameOffset, pages, row); }
        catch (FormatException ex) when (ex.Message.Contains("palette outside loaded texture"))
        { return HudFrameRenderer.RenderFrame(parts, sheetBody, frameOffset, pages, 0); }
    }

    private static byte[] CropCursorFrame(byte[] full, int fullW, int frameIdx, int fw, int fh)
    {
        var result = new byte[fw * fh * 4];
        for (int y = 0; y < fh; y++)
            Buffer.BlockCopy(full, (y * fullW + frameIdx * fw) * 4, result, y * fw * 4, fw * 4);
        return result;
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
