using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>
/// Serializes report sprite metadata without changing the established manifest ordering.
/// </summary>
internal static class BattleReportJsonWriter
{
    /// <summary>
    /// Writes the report manifest with the legacy property and collection order preserved byte for byte.
    /// </summary>
    /// <param name="frames">Rendered report frame metadata in required-frame order.</param>
    /// <param name="arrowRows">Rendered arrow variants in CLUT-row order.</param>
    /// <param name="arrowPartsMeta">The canonical arrow-part geometry taken from the first CLUT row.</param>
    /// <param name="animations">The fixed partner-by-frame animation table.</param>
    /// <param name="sheetArchive">The source report sprite-sheet bytes used for provenance.</param>
    /// <param name="textureArchive">The source report texture bytes used for provenance.</param>
    /// <param name="overlay">The source overlay bytes used for provenance.</param>
    /// <returns>UTF-8 text ending in exactly one line-feed.</returns>
    internal static string Build(
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
                json.WriteStartObject(frameId.ToString(CultureInfo.InvariantCulture));
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
}
