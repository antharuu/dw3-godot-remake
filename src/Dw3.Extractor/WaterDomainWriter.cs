using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Water;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

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
    /// <param name="context">Write context providing the database, transaction, and image-path resolver.</param>
    /// <param name="zones">Decoded water zones in stage-id order; each produces images and database rows.</param>
    /// <param name="skipped">Stages that could not be decoded; recorded in <c>water_skipped_zones</c> and the manifest.</param>
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
        {
            WriteZone(context, json, zone);
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>
    /// Inserts zone-level source hashes and object rows into the SQLite tables, then writes each
    /// object frame as opaque and STP PNG images and records them in the manifest JSON.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="json">JSON writer positioned inside the <c>zones</c> object.</param>
    /// <param name="zone">The decoded water zone to persist.</param>
    private static void WriteZone(DataPackWriteContext context, Utf8JsonWriter json, ExtractedWaterZone zone)
    {
        string name = ZoneName(zone.StageId);
        Insert(
            context,
            "INSERT INTO water_zones (stage_id, overlay_sha256, sheet_sha256, texture_sha256, archive_entries) VALUES ($1, $2, $3, $4, $5)",
            zone.StageId,
            zone.Sources.OverlaySha256,
            zone.Sources.SheetSha256,
            zone.Sources.TextureSha256,
            zone.Objects.ArchiveEntries);

        json.WriteStartObject(name);
        json.WriteStartArray("objects");
        foreach (StageObject item in zone.Objects.Objects)
        {
            WriteObject(context, json, zone.StageId, name, item);
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

    /// <summary>
    /// Inserts one <c>water_objects</c> row for a stage object and writes its manifest entry: the tile
    /// fields, then one frame entry per animation frame in cycle order.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="json">JSON writer positioned inside the zone's <c>objects</c> array.</param>
    /// <param name="stageId">The stage the object belongs to.</param>
    /// <param name="name">The zone folder name, also the image folder under the water domain.</param>
    /// <param name="item">The decoded stage object with its tile record and frames.</param>
    private static void WriteObject(DataPackWriteContext context, Utf8JsonWriter json, int stageId, string name, StageObject item)
    {
        StageTile tile = item.Tile;
        Insert(
            context,
            """
            INSERT INTO water_objects (stage_id, object_index, visible, anim, margin, depth, frame, cycle, cycle_first, cycle_last,
                cycle_delay, clut_row, x, y, sort_y, cycle_time, source_address)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)
            """,
            stageId,
            item.Index,
            tile.Visible,
            tile.Anim,
            tile.Margin,
            tile.Depth,
            tile.Frame,
            tile.Cycle,
            tile.CycleFirst,
            tile.CycleLast,
            tile.CycleDelay,
            tile.ClutRow,
            tile.X,
            tile.Y,
            tile.SortY,
            tile.CycleTime,
            tile.SourceAddress);

        json.WriteStartObject();
        WriteTile(json, tile);
        json.WriteNumber("index", item.Index);
        json.WriteStartArray("frames");
        for (int ordinal = 0; ordinal < item.Frames.Count; ordinal++)
        {
            WriteObjectFrame(context, json, stageId, name, item.Index, ordinal, item.Frames[ordinal]);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>
    /// Writes one animation frame of a stage object twice, as an opaque PNG and as an STP PNG (only
    /// the texels with the STP bit, drawn opaque), inserts its <c>water_object_frames</c> row and its manifest entry.
    /// File names are <c>object-III-frame-FF-clut-R-{opaque|stp}.png</c>.
    /// </summary>
    /// <param name="context">Write context for the destination data pack.</param>
    /// <param name="json">JSON writer positioned inside the object's <c>frames</c> array.</param>
    /// <param name="stageId">The stage the object belongs to.</param>
    /// <param name="name">The zone folder name.</param>
    /// <param name="objectIndex">The index of the object in the stage object table.</param>
    /// <param name="ordinal">The position of the frame in the object's animation cycle.</param>
    /// <param name="frame">The rendered frame with its sprite frame id and CLUT row.</param>
    private static void WriteObjectFrame(
        DataPackWriteContext context,
        Utf8JsonWriter json,
        int stageId,
        string name,
        int objectIndex,
        int ordinal,
        StageObjectFrame frame)
    {
        string stem = $"object-{objectIndex:D3}-frame-{frame.FrameId:X2}-clut-{frame.ClutRow.ToString(CultureInfo.InvariantCulture)}";
        string opaque = stem + "-opaque.png";
        string stp = stem + "-stp.png";
        SpriteFrameImage image = frame.Image;
        WriteImage(context, $"{Domain}/{name}/{opaque}", image.Width, image.Height, image.ToOpaqueRgba());
        WriteImage(context, $"{Domain}/{name}/{stp}", image.Width, image.Height, image.ToStpRgba());
        Insert(
            context,
            """
            INSERT INTO water_object_frames (stage_id, object_index, ordinal, frame_id, clut_row, offset_x, offset_y, abr, opaque_image, stp_image)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """,
            stageId,
            objectIndex,
            ordinal,
            frame.FrameId,
            frame.ClutRow,
            image.OffsetX,
            image.OffsetY,
            image.Abr,
            $"{Domain}/{name}/{opaque}",
            $"{Domain}/{name}/{stp}");

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

    /// <summary>
    /// Writes all numeric properties of <paramref name="tile"/> as individual JSON number fields
    /// to <paramref name="json"/> (anim, clut_row, cycle, cycle_delay, cycle_first, cycle_last,
    /// cycle_time, depth, frame, margin, sort_y, source_address, visible, x, y).
    /// </summary>
    /// <param name="json">JSON writer positioned inside the object that will receive the tile fields.</param>
    /// <param name="tile">The stage tile whose properties are written.</param>
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

    /// <summary>
    /// Encodes <paramref name="rgba"/> as a PNG and writes it to <paramref name="relativePath"/>
    /// in the data pack image tree, creating parent directories as needed.
    /// </summary>
    /// <param name="context">Write context providing the image-path resolver and image registry.</param>
    /// <param name="relativePath">Pack-relative path (e.g. <c>water/S001/object-000-frame-00-clut-0-opaque.png</c>).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="rgba">Raw 32-bit RGBA pixel data, row-major, <c>width × height × 4</c> bytes.</param>
    private static void WriteImage(DataPackWriteContext context, string relativePath, int width, int height, byte[] rgba)
    {
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(width, height, rgba));
        context.RegisterImage(relativePath);
    }

    /// <summary>
    /// Executes <paramref name="sql"/> against the destination database within the current transaction,
    /// binding <paramref name="values"/> to positional parameters <c>$1</c>, <c>$2</c>, and so on.
    /// </summary>
    /// <param name="context">Write context providing the database connection and active transaction.</param>
    /// <param name="sql">SQL command text with positional parameters <c>$1</c>, <c>$2</c>, and so on.</param>
    /// <param name="values">Values bound to parameters in order.</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        }

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the stage directory name for <paramref name="stageId"/>: <c>S</c> followed by the
    /// zero-padded three-digit stage id (e.g. stage 1 → <c>S001</c>).
    /// </summary>
    /// <param name="stageId">Three-digit stage number.</param>
    /// <returns>Stage directory name string.</returns>
    private static string ZoneName(int stageId) => $"S{stageId:D3}";
}
