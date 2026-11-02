using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Persists disc-derived FIELDCOM cue and icon resources.</summary>
public static class FieldcomDomainWriter
{
    private const int FieldcomFileId = 0x160;
    private const string FieldLayer = "FIELD_LAYER_MAP";
    private const string LoopSemantics = "a step whose frame equals 0xFF is a loop sentinel: its duration slot is the step index to jump to (step = anim[step][1]); a trailing {0x00, 0} step after the sentinel is alignment padding never reached";

    /// <summary>
    /// Writes the cue and/or icon FIELDCOM domains to the data pack, including PNG frames,
    /// SQLite rows, and <c>manifest.json</c>. At least one domain must be non-null.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="cue">The cue domain to write, or <see langword="null"/> to skip.</param>
    /// <param name="icon">The icon domain to write, or <see langword="null"/> to skip.</param>
    /// <exception cref="ArgumentException">Both <paramref name="cue"/> and <paramref name="icon"/> are null.</exception>
    public static void Write(DataPackWriteContext context, FieldcomCueDomain? cue, FieldcomIconDomain? icon)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (cue is null && icon is null) throw new ArgumentException("At least one FIELDCOM domain is required.");
        if (cue is not null) WriteCue(context, cue);
        if (icon is not null) WriteIcon(context, icon);
    }

    /// <summary>
    /// Writes the cue domain: SQLite rows for the domain, frames, exit rows, inner sequences,
    /// pop sequences, and the cue manifest JSON.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="cue">The decoded cue domain with frames, exit rows, and animation sequences.</param>
    private static void WriteCue(DataPackWriteContext context, FieldcomCueDomain cue)
    {
        Insert(context, "INSERT INTO fieldcom_domains (domain, disc_path, sha256, bytes) VALUES ($1,$2,$3,$4)", "cue", cue.DiscPath, cue.Sha256, cue.ByteCount);
        WriteFrames(context, "cue", cue.Frames);
        foreach (FieldcomExitRow row in cue.ExitRows) Insert(context, "INSERT INTO cue_exit_rows (direction,row) VALUES ($1,$2)", row.Direction, row.Row);
        WriteCueSequences(context, "inner", cue.InnerSequences);
        WriteCueSequences(context, "pop", cue.PopSequences);
        WriteManifest(context, "cue", cue.DiscPath, cue.Sha256, cue.ByteCount, cue.Frames, writer =>
        {
            writer.WriteStartObject("anchor"); writer.WriteNumber("depth", 1); writer.WriteString("layer", FieldLayer); writer.WriteNumber("y_offset", -27); writer.WriteEndObject();
            writer.WriteStartObject("exit_rows");
            foreach (FieldcomExitRow row in cue.ExitRows.OrderBy(x => x.Direction))
            {
                writer.WriteNumber(row.Direction.ToString(CultureInfo.InvariantCulture), row.Row);
            }

            writer.WriteEndObject();
            writer.WriteStartObject("inner_rows");
            foreach (FieldcomNamedSequence sequence in cue.InnerSequences.OrderBy(x => CueRow(x.Steps)))
            {
                writer.WriteStartArray(CueRow(sequence.Steps).ToString(CultureInfo.InvariantCulture));
                foreach (FieldcomFrameStep step in sequence.Steps) { writer.WriteStartObject(); writer.WriteNumber("duration_ticks", step.Duration); writer.WriteNumber("frame", step.Frame); writer.WriteEndObject(); }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
            FieldcomNamedSequence opening = cue.PopSequences.Single(x => x.Name == "opening");
            FieldcomNamedSequence closing = cue.PopSequences.Single(x => x.Name == "closing");
            writer.WriteStartObject("pop");
            writer.WriteStartArray("closing"); writer.WriteNumberValue(closing.Steps[0].Frame); writer.WriteNumberValue(closing.Steps[^1].Frame); writer.WriteEndArray();
            writer.WriteStartArray("frames");
            foreach (FieldcomExtractedFrame frame in cue.Frames.Where(x => x.FrameId is >= 0x32 and <= 0x37).OrderBy(x => x.FrameId))
            {
                writer.WriteNumberValue(frame.FrameId);
            }

            writer.WriteEndArray();
            writer.WriteNumber("open", 0x35);
            writer.WriteStartArray("opening"); writer.WriteNumberValue(opening.Steps[0].Frame); writer.WriteNumberValue(opening.Steps[^1].Frame); writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// Writes the icon domain: SQLite rows for the domain, frames, animation steps,
    /// and the icon manifest JSON.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="icon">The decoded icon domain with frames and animation table.</param>
    private static void WriteIcon(DataPackWriteContext context, FieldcomIconDomain icon)
    {
        Insert(context, "INSERT INTO fieldcom_domains (domain, disc_path, sha256, bytes) VALUES ($1,$2,$3,$4)", "icon", icon.DiscPath, icon.Sha256, icon.ByteCount);
        WriteFrames(context, "icon", icon.Frames);
        for (int animation = 0; animation < icon.Animations.Count; animation++)
        {
            FieldcomAnimation value = icon.Animations[animation];
            Insert(context, "INSERT INTO icon_animations (animation,loop_target) VALUES ($1,$2)", animation, value.LoopTarget);
            for (int ordinal = 0; ordinal < value.Steps.Count; ordinal++) Insert(context, "INSERT INTO icon_animation_steps (animation,ordinal,frame_id,duration) VALUES ($1,$2,$3,$4)", animation, ordinal, value.Steps[ordinal].Frame, value.Steps[ordinal].Duration);
        }
        WriteManifest(context, "icon", icon.DiscPath, icon.Sha256, icon.ByteCount, icon.Frames, writer =>
        {
            writer.WriteStartObject("anchor"); writer.WriteNumber("depth", 2); writer.WriteString("layer", FieldLayer); writer.WriteStartArray("offset"); writer.WriteNumberValue(4); writer.WriteNumberValue(-36); writer.WriteEndArray(); writer.WriteEndObject();
            writer.WriteStartArray("animations");
            for (int i = 0; i < icon.Animations.Count; i++)
            {
                FieldcomAnimation animation = icon.Animations[i]; writer.WriteStartObject(); writer.WriteString("name", $"iconAnim{i}"); writer.WriteStartArray("steps");
                foreach (FieldcomFrameStep step in animation.Steps) { writer.WriteStartArray(); writer.WriteNumberValue(step.Frame); writer.WriteNumberValue(step.Duration); writer.WriteEndArray(); }
                writer.WriteStartArray(); writer.WriteNumberValue(0xFF); writer.WriteNumberValue(animation.LoopTarget); writer.WriteEndArray();
                if (animation.HasTrailingPadding) { writer.WriteStartArray(); writer.WriteNumberValue(0); writer.WriteNumberValue(0); writer.WriteEndArray(); }
                writer.WriteEndArray(); writer.WriteNumber("substate", i); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("loop_semantics", LoopSemantics);
        });
    }

    /// <summary>
    /// Encodes each frame as a PNG, registers it with the data pack, and inserts a row into <c>fieldcom_frames</c>.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="domain">The domain name (<c>"cue"</c> or <c>"icon"</c>) used as the output subdirectory.</param>
    /// <param name="frames">The decoded frames in frame-id order.</param>
    private static void WriteFrames(DataPackWriteContext context, string domain, IReadOnlyList<FieldcomExtractedFrame> frames)
    {
        foreach (FieldcomExtractedFrame frame in frames.OrderBy(x => x.FrameId))
        {
            string relative = $"{domain}/frame-{frame.FrameId:X2}.png"; string path = context.GetImagePath(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, PngEncoder.Encode(frame.Width, frame.Height, frame.RgbaPixels)); context.RegisterImage(relative);
            Insert(context, "INSERT INTO fieldcom_frames (domain,frame_id,width,height,offset_x,offset_y,row,abr,image_path) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)", domain, frame.FrameId, frame.Width, frame.Height, frame.OffsetX, frame.OffsetY, frame.Row, frame.Abr, relative);
        }
    }

    // Inner sequences carry the field row the cue selects them by; pop sequences have none.
    /// <summary>
    /// Inserts one row per animation step into <c>cue_sequences</c> for all named cue sequences.
    /// Inner sequences carry the field row derived from their first frame; pop sequences use NULL.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="kind">Either <c>"inner"</c> or <c>"pop"</c>; determines whether a field row is stored.</param>
    /// <param name="sequences">The named sequences to insert.</param>
    private static void WriteCueSequences(DataPackWriteContext context, string kind, IReadOnlyList<FieldcomNamedSequence> sequences)
    {
        foreach (FieldcomNamedSequence sequence in sequences)
        {
            object fieldRow = kind == "inner" ? CueRow(sequence.Steps) : DBNull.Value;
            for (int ordinal = 0; ordinal < sequence.Steps.Count; ordinal++) Insert(context, "INSERT INTO cue_sequences (kind,name,ordinal,frame_id,duration,field_row) VALUES ($1,$2,$3,$4,$5,$6)", kind, sequence.Name, ordinal, sequence.Steps[ordinal].Frame, sequence.Steps[ordinal].Duration, fieldRow);
        }
    }

    /// <summary>
    /// Writes the FIELDCOM manifest JSON to <c>&lt;domain&gt;/manifest.json</c> with domain-specific
    /// payload (anchor, sequences/animations, and schema identifier) followed by a frames object.
    /// </summary>
    /// <param name="context">The data pack write context providing image path resolution.</param>
    /// <param name="domain">The domain name (<c>"cue"</c> or <c>"icon"</c>) used as the output subdirectory.</param>
    /// <param name="discPath">The ISO path of the source archive file.</param>
    /// <param name="sha256">The SHA-256 hex digest of the source archive.</param>
    /// <param name="bytes">The byte length of the source archive.</param>
    /// <param name="frames">The decoded frames to include in the frames object.</param>
    /// <param name="payload">A callback that writes additional domain-specific JSON properties before the frames object.</param>
    private static void WriteManifest(DataPackWriteContext context, string domain, string discPath, string sha256, int bytes, IReadOnlyList<FieldcomExtractedFrame> frames, Action<Utf8JsonWriter> payload)
    {
        string path = context.GetImagePath($"{domain}/manifest.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var stream = File.Create(path); using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }); json.WriteStartObject(); payload(json); json.WriteStartObject("frames");
        foreach (FieldcomExtractedFrame frame in frames.OrderBy(x => x.FrameId)) { json.WriteStartObject(frame.FrameId.ToString(CultureInfo.InvariantCulture)); json.WriteNumber("abr", frame.Abr); json.WriteStartArray("offset"); json.WriteNumberValue(frame.OffsetX); json.WriteNumberValue(frame.OffsetY); json.WriteEndArray(); json.WriteString("path", $"frame-{frame.FrameId:X2}.png"); json.WriteNumber("row", frame.Row); json.WriteStartArray("size"); json.WriteNumberValue(frame.Width); json.WriteNumberValue(frame.Height); json.WriteEndArray(); json.WriteEndObject(); }
        json.WriteEndObject(); json.WriteString("schema", domain == "cue" ? "m1-exit-cue-v1" : "m5-icon-v1"); json.WriteStartObject("source"); json.WriteStartObject("archive_entries"); json.WriteNumber("sheet", 0); json.WriteStartArray("vram_pages"); json.WriteNumberValue(2); json.WriteNumberValue(3); json.WriteEndArray(); json.WriteEndObject(); json.WriteNumber("bytes", bytes); json.WriteNumber("file_id", FieldcomFileId); json.WriteString("path", discPath); json.WriteString("sha256", sha256); json.WriteEndObject(); json.WriteEndObject();
    }

    /// <summary>
    /// Returns the field row index (6..10) for a cue inner sequence by matching the first frame id
    /// to the five known cue exit-row frame identifiers.
    /// </summary>
    /// <param name="steps">The animation steps of the sequence; the first step's frame determines the row.</param>
    /// <returns>The field row index (6..10).</returns>
    /// <exception cref="FormatException">The first frame id does not match any known cue row frame.</exception>
    private static int CueRow(IReadOnlyList<FieldcomFrameStep> steps) => steps[0].Frame switch { 0x10 => 6, 0x0E => 7, 0x0C => 8, 0x12 => 9, 0x45 => 10, _ => throw new FormatException("Unknown FIELDCOM cue row frame.") };

    /// <summary>
    /// Executes a parameterised INSERT command against the current pack transaction.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="sql">The parameterised SQL text using positional parameters ($1, $2, …).</param>
    /// <param name="values">The parameter values in positional order.</param>
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
}
