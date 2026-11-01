using System.Globalization;
using System.Text.Json;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Serializes zone manifest data to the canonical JSON format.
/// All fields are written in alphabetical key order to match the baseline sort.
/// Paths are ISO-relative disc paths — never host filesystem paths.
/// </summary>
public static class ZoneManifestWriter
{
    /// <summary>Writes a complete zone manifest from a <see cref="ZoneManifestData"/> instance.</summary>
    public static void Write(TextWriter output, ZoneManifestData data)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);

        using var writer = new Utf8JsonWriter(new StreamWriterAdapter(output), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();

        // Keys written in alphabetical order to match the baseline manifest sort.
        WriteMapSection(writer, "areas", data.AreasMap ?? ZeroMap(data.FloorMap.Width, data.FloorMap.Height));
        WriteExits(writer, data.Slots, data.StageModes);
        WriteMapSection(writer, "floor", data.FloorMap);
        WriteMapEntriesSource(writer, data);
        WriteMapSource(writer, data);
        WriteRender(writer, data);
        WriteSlots(writer, data.Slots);
        writer.WriteString("status", data.Status);
        WriteMapSection(writer, "triggers", data.TriggerMap ?? ZeroMap(data.FloorMap.Width, data.FloorMap.Height));
        writer.WriteString("zone", data.Zone);

        writer.WriteEndObject();
        writer.Flush();
    }

    private static TmpkMap ZeroMap(int width, int height) =>
        new(width, height, new byte[checked(width * height)]);

    private static void WriteMapSection(Utf8JsonWriter writer, string key, TmpkMap map)
    {
        Dictionary<byte, int> histogram = BuildHistogram(map.Cells);
        writer.WriteStartObject(key);
        writer.WriteStartObject("histogram");
        foreach (var (value, count) in histogram.OrderBy(kv => kv.Key))
        {
            writer.WriteNumber(value.ToString(CultureInfo.InvariantCulture), count);
        }

        writer.WriteEndObject();
        writer.WriteStartArray("size");
        writer.WriteNumberValue(map.Width);
        writer.WriteNumberValue(map.Height);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static Dictionary<byte, int> BuildHistogram(byte[] cells)
    {
        var result = new Dictionary<byte, int>();
        foreach (byte b in cells)
        {
            if (!result.TryGetValue(b, out int count))
            {
                count = 0;
            }

            result[b] = count + 1;
        }

        return result;
    }

    private static void WriteExits(Utf8JsonWriter writer, IReadOnlyList<StageSlot> slots, IReadOnlyDictionary<ushort, int>? stageModes)
    {
        writer.WriteStartArray("exits");
        for (int index = 0; index < slots.Count; index++)
        {
            StageSlot slot = slots[index];
            if (slot.Type != 1)
            {
                continue;
            }

            writer.WriteStartObject();
            if (slot.Condition != ushort.MaxValue)
            {
                writer.WriteNumber("condition", slot.Condition);
            }

            WriteCondition(writer, slot.Condition);
            // Write null when the mode has no known disc stage mapping (e.g. excluded or unreachable stages).
            if (stageModes is not null && stageModes.TryGetValue(slot.Argument, out int destStage))
            {
                writer.WriteString("destination", $"S{destStage:D3}");
            }
            else
            {
                writer.WriteNull("destination");
            }

            writer.WriteNumber("facing", slot.Direction);
            writer.WriteNumber("slot", index);
            writer.WriteNumber("x", slot.X);
            writer.WriteNumber("y", slot.Y);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMapEntriesSource(Utf8JsonWriter writer, ZoneManifestData data)
    {
        writer.WriteStartObject("map_entries_source");
        if (data.AreasEntry is int areasEntry)
        {
            writer.WriteNumber("areas_entry", areasEntry);
        }
        else
        {
            writer.WriteNull("areas_entry");
        }

        writer.WriteNumber("bytes", data.TmpkBytes);
        writer.WriteNumber("floor_entry", data.FloorEntry);
        writer.WriteString("path", data.TmpkPath);
        writer.WriteStartArray("required_offsets");
        foreach (int offset in data.RequiredOffsets)
        {
            writer.WriteNumberValue(offset);
        }

        writer.WriteEndArray();
        writer.WriteString("sha256", data.TmpkSha256);
        if (data.TriggerEntry is int triggerEntry)
        {
            writer.WriteNumber("trigger_entry", triggerEntry);
        }
        else
        {
            writer.WriteNull("trigger_entry");
        }

        writer.WriteEndObject();
    }

    private static void WriteMapSource(Utf8JsonWriter writer, ZoneManifestData data)
    {
        writer.WriteStartObject("map_source");
        writer.WriteNumber("bytes", data.PackBytes);
        writer.WriteStartObject("header");
        // The legacy cache stores the packet header dimensions unless a TMPK map entry was
        // absent. In that fallback case its manifest builder reused the floor-map pixel
        // dimensions. Preserve that established cache contract for byte-for-byte semantics.
        bool usesFloorPixels = data.TriggerEntry is null || data.AreasEntry is null;
        writer.WriteNumber("height_tiles", usesFloorPixels ? data.FloorMap.Height : data.TileRows);
        writer.WriteNumber("tile_bytes", data.TilePacketBytes);
        writer.WriteNumber("tile_count", data.TileCount);
        writer.WriteNumber("width_tiles", usesFloorPixels ? data.FloorMap.Width : data.TileColumns);
        writer.WriteEndObject();
        writer.WriteString("path", data.PackPath);
        writer.WriteString("sha256", data.PackSha256);
        writer.WriteEndObject();
    }

    private static void WriteRender(Utf8JsonWriter writer, ZoneManifestData data)
    {
        writer.WriteStartObject("render");
        writer.WriteString("composite", "composite-back-to-front.png");
        writer.WriteStartArray("depth_layers");
        writer.WriteStringValue("depth-0.png");
        writer.WriteStringValue("depth-1.png");
        writer.WriteStringValue("depth-2.png");
        writer.WriteEndArray();
        if (data.StreamDepth11Modulate is not null)
        {
            writer.WriteStartArray("stream_depth11_modulate");
            foreach (byte b in data.StreamDepth11Modulate)
            {
                writer.WriteNumberValue(b);
            }

            writer.WriteEndArray();
        }
        else
        {
            writer.WriteNull("stream_depth11_modulate");
        }

        writer.WriteStartArray("tile_records");
        foreach (ZoneTileRecord rec in data.TileRecords)
        {
            WriteTileRecord(writer, rec);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTileRecord(Utf8JsonWriter writer, ZoneTileRecord rec)
    {
        writer.WriteStartObject();
        if (rec.Present)
        {
            writer.WriteNumber("decoded_bytes", rec.DecodedBytes);
            writer.WriteBoolean("present", true);
            writer.WriteBoolean("rlen", rec.Rlen);
            writer.WriteStartArray("sprites");
            if (rec.Sprites is not null)
            {
                foreach (ZoneSpriteRecord sprite in rec.Sprites)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("depth", sprite.Depth);
                    writer.WriteNumber("h", sprite.H);
                    writer.WriteNumber("u", sprite.U);
                    writer.WriteNumber("v", sprite.V);
                    writer.WriteNumber("w", sprite.W);
                    writer.WriteNumber("x", sprite.X);
                    writer.WriteNumber("y", sprite.Y);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteNumber("tile", rec.Tile);
            writer.WriteNumber("tim_offset", rec.TimOffset);
            writer.WriteStartArray("tim_size");
            writer.WriteNumberValue(rec.TimWidth);
            writer.WriteNumberValue(rec.TimHeight);
            writer.WriteEndArray();
        }
        else
        {
            writer.WriteBoolean("present", false);
            writer.WriteNumber("tile", rec.Tile);
        }

        writer.WriteEndObject();
    }

    private static void WriteSlots(Utf8JsonWriter writer, IReadOnlyList<StageSlot> slots)
    {
        writer.WriteStartArray("slots");
        foreach (StageSlot slot in slots)
        {
            writer.WriteStartObject();
            writer.WriteNumber("arg", slot.Argument);
            writer.WriteNumber("condition", slot.Condition);
            WriteCondition(writer, slot.Condition);
            writer.WriteNumber("dir", slot.Direction);
            writer.WriteNumber("type", slot.Type);
            writer.WriteNumber("x", slot.X);
            writer.WriteNumber("y", slot.Y);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteCondition(Utf8JsonWriter writer, ushort condition) =>
        writer.WriteString("condition_status", condition == ushort.MaxValue ? "unconditional" : "unevaluated");

    private sealed class StreamWriterAdapter(TextWriter writer) : Stream
    {
        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <inheritdoc/>
        public override void Flush() => writer.Flush();

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => writer.Write(System.Text.Encoding.UTF8.GetString(buffer, offset, count));

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer) => writer.Write(System.Text.Encoding.UTF8.GetString(buffer));

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
