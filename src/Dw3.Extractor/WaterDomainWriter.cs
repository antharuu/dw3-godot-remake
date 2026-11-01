using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Water;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Disc-file digests recorded with a decoded stage.</summary>
public sealed record WaterZoneSources(string OverlaySha256, string SheetSha256, string TextureSha256);

/// <summary>A stage whose map objects decoded successfully.</summary>
public sealed record ExtractedWaterZone(int StageId, StageObjectSet Objects, WaterZoneSources Sources);

/// <summary>A stage whose map-object files use a layout the decoder rejects.</summary>
public sealed record SkippedWaterZone(int StageId, string Error);

/// <summary>
/// Writes the map-object ("water") domain: SQL tables, one opaque and one STP image per
/// rendered frame under <c>water/Snnn/</c>, and <c>water/manifest.json</c>.
/// </summary>
public static class WaterDomainWriter
{
    /// <summary>Manifest schema identifier read by the runtime.</summary>
    public const string ManifestSchema = "m1-stage-water-v3";

    private const string Domain = "water";

    /// <summary>Persists decoded and skipped stages in stage order.</summary>
    public static void Write(DataPackWriteContext context, IReadOnlyList<ExtractedWaterZone> zones, IReadOnlyList<SkippedWaterZone> skipped)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(skipped);

        string manifestPath = context.GetImagePath($"{Domain}/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        using var stream = File.Create(manifestPath);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("schema", ManifestSchema);
        json.WriteStartArray("skipped_zones");
        foreach (SkippedWaterZone zone in skipped)
        {
            Insert(context, "INSERT INTO water_skipped_zones (stage_id, error) VALUES ($1, $2)", zone.StageId, zone.Error);
            json.WriteStringValue($"{ZoneName(zone.StageId)}: {zone.Error}");
        }
        json.WriteEndArray();
        json.WriteStartObject("zones");
        foreach (ExtractedWaterZone zone in zones)
            WriteZone(context, json, zone);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteZone(DataPackWriteContext context, Utf8JsonWriter json, ExtractedWaterZone zone)
    {
        string name = ZoneName(zone.StageId);
        Insert(context, "INSERT INTO water_zones (stage_id, overlay_sha256, sheet_sha256, texture_sha256, archive_entries) VALUES ($1, $2, $3, $4, $5)",
            zone.StageId, zone.Sources.OverlaySha256, zone.Sources.SheetSha256, zone.Sources.TextureSha256, zone.Objects.ArchiveEntries);

        json.WriteStartObject(name);
        json.WriteStartArray("objects");
        foreach (StageObject item in zone.Objects.Objects)
        {
            StageTile tile = item.Tile;
            Insert(context, """
                INSERT INTO water_objects (stage_id, object_index, visible, anim, margin, depth, frame, cycle, cycle_first, cycle_last,
                    cycle_delay, clut_row, x, y, sort_y, cycle_time, source_address)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)
                """,
                zone.StageId, item.Index, tile.Visible, tile.Anim, tile.Margin, tile.Depth, tile.Frame, tile.Cycle, tile.CycleFirst,
                tile.CycleLast, tile.CycleDelay, tile.ClutRow, tile.X, tile.Y, tile.SortY, tile.CycleTime, tile.SourceAddress);

            json.WriteStartObject();
            WriteTile(json, tile);
            json.WriteNumber("index", item.Index);
            json.WriteStartArray("frames");
            for (int ordinal = 0; ordinal < item.Frames.Count; ordinal++)
            {
                StageObjectFrame frame = item.Frames[ordinal];
                string stem = $"object-{item.Index:D3}-frame-{frame.FrameId:X2}-clut-{frame.ClutRow.ToString(CultureInfo.InvariantCulture)}";
                string opaque = stem + "-opaque.png";
                string stp = stem + "-stp.png";
                SpriteFrameImage image = frame.Image;
                WriteImage(context, $"{Domain}/{name}/{opaque}", image.Width, image.Height, image.ToOpaqueRgba());
                WriteImage(context, $"{Domain}/{name}/{stp}", image.Width, image.Height, image.ToStpRgba());
                Insert(context, """
                    INSERT INTO water_object_frames (stage_id, object_index, ordinal, frame_id, clut_row, offset_x, offset_y, abr, opaque_image, stp_image)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
                    """,
                    zone.StageId, item.Index, ordinal, frame.FrameId, frame.ClutRow, image.OffsetX, image.OffsetY, image.Abr,
                    $"{Domain}/{name}/{opaque}", $"{Domain}/{name}/{stp}");

                json.WriteStartObject();
                json.WriteNumber("abr", image.Abr);
                json.WriteStartArray("offset");
                json.WriteNumberValue(image.OffsetX);
                json.WriteNumberValue(image.OffsetY);
                json.WriteEndArray();
                json.WriteString("opaque_path", opaque);
                json.WriteString("semi_path", stp);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartObject("sources");
        json.WriteNumber("archive_entries", zone.Objects.ArchiveEntries);
        json.WriteString("overlay_sha256", zone.Sources.OverlaySha256);
        json.WriteString("sheet_sha256", zone.Sources.SheetSha256);
        json.WriteString("texture_sha256", zone.Sources.TextureSha256);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteTile(Utf8JsonWriter json, StageTile tile)
    {
        json.WriteNumber("anim", tile.Anim);
        json.WriteNumber("clut_row", tile.ClutRow);
        json.WriteNumber("cycle", tile.Cycle);
        json.WriteNumber("cycle_delay", tile.CycleDelay);
        json.WriteNumber("cycle_first", tile.CycleFirst);
        json.WriteNumber("cycle_last", tile.CycleLast);
        json.WriteNumber("cycle_time", tile.CycleTime);
        json.WriteNumber("depth", tile.Depth);
        json.WriteNumber("frame", tile.Frame);
        json.WriteNumber("margin", tile.Margin);
        json.WriteNumber("sort_y", tile.SortY);
        json.WriteNumber("source_address", tile.SourceAddress);
        json.WriteNumber("visible", tile.Visible);
        json.WriteNumber("x", tile.X);
        json.WriteNumber("y", tile.Y);
    }

    private static void WriteImage(DataPackWriteContext context, string relativePath, int width, int height, byte[] rgba)
    {
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(width, height, rgba));
        context.RegisterImage(relativePath);
    }

    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        command.ExecuteNonQuery();
    }

    private static string ZoneName(int stageId) => $"S{stageId:D3}";
}
