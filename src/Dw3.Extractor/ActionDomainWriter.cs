using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>One decoded TIM frame from a player action archive.</summary>
public sealed record ActionFrame(int FrameId, int Width, int Height, byte[] RgbaPixels);

/// <summary>One action set resolved from the archive's five directional references.</summary>
public sealed record ActionSet(int SetId, string Name, ActionSequenceTerminator Terminator, IReadOnlyList<ActionAnimationRecord> Records);

/// <summary>Disc-derived action archive metadata and decoded output for one domain.</summary>
public sealed record ActionDomain(
    string Name,
    string ManifestSchema,
    int FileId,
    int AnimationSetEntry,
    string DiscPath,
    string Sha256,
    int ByteCount,
    IReadOnlyList<ActionFrame> Frames,
    IReadOnlyList<ActionSet> Sets);

/// <summary>Writes a player action domain as PNG frames, SQLite records, and its cache manifest.</summary>
public static class ActionDomainWriter
{
    /// <summary>Writes one domain beneath its domain name in the data pack.</summary>
    public static void Write(DataPackWriteContext context, ActionDomain domain)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domain);
        if (string.IsNullOrWhiteSpace(domain.Name) || string.IsNullOrWhiteSpace(domain.ManifestSchema))
            throw new ArgumentException("Action domain name and manifest schema are required.", nameof(domain));

        Insert(context, "INSERT INTO action_domains (domain, file_id, anim_set_entry, manifest_schema, disc_path, sha256, bytes) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            domain.Name, domain.FileId, domain.AnimationSetEntry, domain.ManifestSchema, domain.DiscPath, domain.Sha256, domain.ByteCount);

        foreach (ActionFrame frame in domain.Frames.OrderBy(frame => frame.FrameId))
            WriteFrame(context, domain.Name, frame);
        foreach (ActionSet set in domain.Sets.OrderBy(set => set.SetId))
            WriteSet(context, domain.Name, set);

        WriteManifest(context, domain);
    }

    private static void WriteFrame(DataPackWriteContext context, string domain, ActionFrame frame)
    {
        string relativePath = $"{domain}/frames/frame-{frame.FrameId:X2}.png";
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(frame.Width, frame.Height, frame.RgbaPixels));
        context.RegisterImage(relativePath);
        Insert(context, "INSERT INTO action_frames (domain, frame_id, width, height, image_path) VALUES ($1, $2, $3, $4, $5)",
            domain, frame.FrameId, frame.Width, frame.Height, relativePath);
    }

    private static void WriteSet(DataPackWriteContext context, string domain, ActionSet set)
    {
        Insert(context, "INSERT INTO action_sets (domain, set_id, name, terminator) VALUES ($1, $2, $3, $4)",
            domain, set.SetId, set.Name, TerminatorName(set.Terminator));
        for (int ordinal = 0; ordinal < set.Records.Count; ordinal++)
        {
            ActionAnimationRecord record = set.Records[ordinal];
            Insert(context, "INSERT INTO action_records (domain, set_id, ordinal, duration, frame_id, offset_x, offset_y) VALUES ($1, $2, $3, $4, $5, $6, $7)",
                domain, set.SetId, ordinal, record.Duration, record.Frame, record.OffsetX, record.OffsetY);
        }
    }

    private static void WriteManifest(DataPackWriteContext context, ActionDomain domain)
    {
        string path = context.GetImagePath($"{domain.Name}/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteStartObject("frames");
        foreach (ActionFrame frame in domain.Frames.OrderBy(frame => frame.FrameId))
        {
            json.WriteStartObject(frame.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            json.WriteStartArray("size");
            json.WriteNumberValue(frame.Width);
            json.WriteNumberValue(frame.Height);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndObject();
        json.WriteString("schema", domain.ManifestSchema);
        json.WriteStartArray("sets");
        foreach (ActionSet set in domain.Sets.OrderBy(set => set.SetId))
        {
            json.WriteStartObject();
            json.WriteString("name", set.Name);
            json.WriteStartArray("records");
            foreach (ActionAnimationRecord record in set.Records)
            {
                json.WriteStartObject();
                json.WriteNumber("duration", record.Duration);
                json.WriteNumber("frame", record.Frame);
                json.WriteStartArray("offset");
                json.WriteNumberValue(record.OffsetX);
                json.WriteNumberValue(record.OffsetY);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteNumber("set", set.SetId);
            json.WriteString("terminator", TerminatorName(set.Terminator));
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteStartObject("source");
        json.WriteNumber("anim_set_entry", domain.AnimationSetEntry);
        json.WriteNumber("bytes", domain.ByteCount);
        json.WriteString("disc_path", domain.DiscPath);
        json.WriteNumber("entry_count", domain.AnimationSetEntry + 1);
        json.WriteNumber("file_number", domain.FileId);
        json.WriteString("path", domain.DiscPath);
        json.WriteString("sha256", domain.Sha256);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static string TerminatorName(ActionSequenceTerminator terminator) =>
        terminator == ActionSequenceTerminator.Loop ? "loop" : "end";

    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        command.ExecuteNonQuery();
    }
}
