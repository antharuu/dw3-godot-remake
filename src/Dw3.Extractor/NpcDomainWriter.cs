using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// Writes a decoded NPC character archive as PNG frames, SQLite records, and a cache manifest.
/// Each character is stored under npc/&lt;stage_id&gt;/actor-&lt;index&gt;/ in the data pack.
/// </summary>
public static class NpcDomainWriter
{
    /// <summary>One row per decoded NPC character: the actor, its stage and the sprite archive entry it was read from.</summary>
    private const string InsertCharacterSql = """
        INSERT INTO npc_characters
            (actor_id, stage_id, actor_index, manifest_schema,
             archive_disc_path, archive_sha256, archive_bytes, archive_entry, frame_count)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
        """;

    private static readonly System.Text.Json.JsonSerializerOptions ManifestSerializerOptions =
        new() { WriteIndented = true };

    /// <summary>Writes the disc-derived actor placement manifest and normalized SQLite rows.</summary>
    /// <param name="context">Active write context with open SQLite transaction and image registry.</param>
    /// <param name="zones">All decoded NPC zones to persist, in stage-ID order.</param>
    /// <param name="snapshotProgress">Progress token written to the manifest JSON snapshot field.</param>
    public static void WritePlacements(
        DataPackWriteContext context,
        IReadOnlyList<NpcExtractedZone> zones,
        int snapshotProgress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(zones);

        foreach (NpcExtractedZone zone in zones.OrderBy(item => item.StageId))
        {
            foreach (NpcExtractedActor actor in zone.Actors.OrderBy(item => item.ActorIndex))
            {
                InsertActorRecord(context, zone.StageId, actor);
                InsertActorConditionRows(context, zone.StageId, actor.ActorIndex, actor.Conditions);
            }
        }

        WritePlacementsJson(context, zones, snapshotProgress);
    }

    /// <summary>Writes one NPC character beneath its stage and actor-index path in the data pack.</summary>
    /// <param name="context">Active write context with open SQLite transaction and image registry.</param>
    /// <param name="character">Decoded character archive to persist as PNG frames and SQLite rows.</param>
    public static void Write(DataPackWriteContext context, NpcExtractedCharacter character)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(character);
        if (string.IsNullOrWhiteSpace(character.ManifestSchema))
        {
            throw new ArgumentException("NPC character manifest schema is required.", nameof(character));
        }

        string prefix = $"npc/{character.StageId}/actor-{character.ActorIndex}";
        InsertCharacter(context, character, prefix);

        foreach (NpcExtractedFrame frame in character.Frames.OrderBy(f => f.FrameId))
        {
            WriteFrame(context, character, prefix, frame);
        }

        foreach (NpcAnimSet set in character.AnimSets.OrderBy(s => s.SetId))
        {
            WriteAnimSet(context, character, set);
        }

        foreach (NpcAnimSequence seq in character.Sequences.OrderBy(s => s.SequenceIndex))
        {
            WriteSequence(context, character, seq);
        }

        WriteManifest(context, character, prefix);
    }

    /// <summary>Writes a single actor placement row to the npc_actors SQLite table.</summary>
    /// <param name="context">Active write context with open SQLite transaction.</param>
    /// <param name="stageId">Disc stage ID to which the actor belongs.</param>
    /// <param name="actor">Decoded actor entry with position and sprite reference fields.</param>
    private static void InsertActorRecord(DataPackWriteContext context, int stageId, NpcExtractedActor actor)
    {
        Insert(
            context,
            "INSERT INTO npc_actors (stage_id, actor_index, actor_id, image, x, y, direction, width, is_player_lead, snapshot_visible, file_number, archive_entry, archive_disc_path) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)",
            stageId,
            actor.ActorIndex,
            actor.ActorId,
            actor.Image,
            actor.X,
            actor.Y,
            actor.Direction,
            actor.Width is null ? DBNull.Value : actor.Width.Value,
            actor.IsPlayerLead ? 1 : 0,
            actor.SnapshotVisible ? 1 : 0,
            actor.FileEntry is null ? DBNull.Value : actor.FileEntry.FileNumber,
            actor.FileEntry is null ? DBNull.Value : actor.FileEntry.ArchiveEntry,
            actor.ArchiveDiscPath is null ? DBNull.Value : actor.ArchiveDiscPath);
    }

    /// <summary>Writes all condition rows for one actor to the npc_actor_conditions table.</summary>
    /// <param name="context">Active write context with open SQLite transaction.</param>
    /// <param name="stageId">Stage ID used as the row's foreign key.</param>
    /// <param name="actorIndex">Actor index used as the row's foreign key.</param>
    /// <param name="conditions">Decoded condition list from the stage overlay.</param>
    private static void InsertActorConditionRows(
        DataPackWriteContext context,
        int stageId,
        int actorIndex,
        IReadOnlyList<NpcCondition> conditions)
    {
        for (int ordinal = 0; ordinal < conditions.Count; ordinal++)
        {
            NpcCondition condition = conditions[ordinal];
            Insert(
                context,
                "INSERT INTO npc_actor_conditions (stage_id, actor_index, ordinal, code, value) VALUES ($1,$2,$3,$4,$5)",
                stageId,
                actorIndex,
                ordinal,
                condition.Code,
                condition.Value);
        }
    }

    /// <summary>Writes the zone placement manifest JSON file under npc/ in the data pack.</summary>
    /// <param name="context">Active write context supplying the image-path resolver.</param>
    /// <param name="zones">All decoded zones, ordered by stage ID in the JSON output.</param>
    /// <param name="snapshotProgress">Progress token written to the snapshot_progress field.</param>
    private static void WritePlacementsJson(
        DataPackWriteContext context,
        IReadOnlyList<NpcExtractedZone> zones,
        int snapshotProgress)
    {
        string path = context.GetImagePath("npc/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("schema", "m7b-npc-v1");
        json.WriteNumber("snapshot_progress", snapshotProgress);
        json.WriteStartObject("zones");
        foreach (NpcExtractedZone zone in zones.OrderBy(item => item.StageId))
        {
            WriteZoneJsonObject(json, zone);
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>Writes one stage zone object (WSTAG key) into the placements manifest JSON.</summary>
    /// <param name="json">JSON writer positioned at the zones object start.</param>
    /// <param name="zone">Decoded zone whose actors and source data are serialized.</param>
    private static void WriteZoneJsonObject(Utf8JsonWriter json, NpcExtractedZone zone)
    {
        json.WriteStartObject($"WSTAG{zone.StageId}");
        json.WriteString("name", $"WSTAG{zone.StageId}");
        json.WriteNumber("stage", zone.StageId);
        json.WriteStartObject("source");
        json.WriteString("pro", zone.OverlayDiscPath);
        json.WriteString("sha256", zone.OverlaySha256);
        json.WriteEndObject();
        json.WriteStartArray("actors");
        foreach (NpcExtractedActor actor in zone.Actors.OrderBy(item => item.ActorIndex))
        {
            WriteActorJsonObject(json, actor);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>Writes one actor object entry into the placements manifest JSON actors array.</summary>
    /// <param name="json">JSON writer positioned inside the actors array.</param>
    /// <param name="actor">Decoded actor entry whose fields and conditions are serialized.</param>
    private static void WriteActorJsonObject(Utf8JsonWriter json, NpcExtractedActor actor)
    {
        json.WriteStartObject();
        json.WriteString("name", $"actor{actor.ActorIndex}");
        json.WriteNumber("id", actor.ActorId);
        json.WriteNumber("image", actor.Image);
        json.WriteNumber("x", actor.X);
        json.WriteNumber("y", actor.Y);
        json.WriteNumber("dir", actor.Direction);
        if (actor.Width is int width)
        {
            json.WriteNumber("width", width);
        }
        else
        {
            json.WriteNull("width");
        }

        json.WriteString("role", actor.IsPlayerLead ? "player-lead" : "npc");
        json.WriteBoolean("snapshot_visible", actor.SnapshotVisible);
        json.WriteStartArray("conditions");
        foreach (NpcCondition condition in actor.Conditions)
        {
            json.WriteStartObject();
            json.WriteNumber("code", condition.Code);
            json.WriteNumber("value", condition.Value);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteActorFileEntryJson(json, actor);
        json.WriteEndObject();
    }

    /// <summary>Writes the file-entry section (file, archive_entry, file_name, sprite) for one actor.</summary>
    /// <param name="json">JSON writer positioned after the conditions array.</param>
    /// <param name="actor">Actor whose optional sprite-archive reference is serialized.</param>
    private static void WriteActorFileEntryJson(Utf8JsonWriter json, NpcExtractedActor actor)
    {
        if (actor.FileEntry is NpcFileEntry entry)
        {
            json.WriteNumber("file", entry.FileNumber);
            json.WriteNumber("archive_entry", entry.ArchiveEntry);
            json.WriteString("file_name", actor.ArchiveDiscPath);
            json.WriteString("sprite", "archive");
        }
        else
        {
            json.WriteNull("file");
            json.WriteNull("archive_entry");
            json.WriteNull("file_name");
            json.WriteString("sprite", "none");
        }
    }

    /// <summary>
    /// Inserts one row into <c>npc_characters</c> for the given NPC character.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="c">The extracted NPC character record to insert.</param>
    /// <param name="prefix">Unused prefix parameter retained for call-site uniformity.</param>
    private static void InsertCharacter(DataPackWriteContext context, NpcExtractedCharacter c, string prefix)
    {
        _ = prefix;
        Insert(
            context,
            InsertCharacterSql,
            c.ActorId,
            c.StageId,
            c.ActorIndex,
            c.ManifestSchema,
            c.ArchiveDiscPath,
            c.ArchiveSha256,
            c.ArchiveBytes,
            c.ArchiveEntry,
            c.Frames.Count);
    }

    /// <summary>
    /// Encodes one animation frame as a PNG, registers it with the data pack, and inserts
    /// a row into <c>npc_frames</c> with width, height, and image path.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="c">The character the frame belongs to; provides actor id, stage id, and actor index.</param>
    /// <param name="prefix">The relative subdirectory under the data pack root (e.g. <c>npc/S200/0</c>).</param>
    /// <param name="frame">The decoded frame with pixel data and display offsets.</param>
    private static void WriteFrame(DataPackWriteContext context, NpcExtractedCharacter c, string prefix, NpcExtractedFrame frame)
    {
        string relativePath = $"{prefix}/frames/frame-{frame.FrameId:X2}.png";
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(frame.Width, frame.Height, frame.RgbaPixels));
        context.RegisterImage(relativePath);
        Insert(
            context,
            "INSERT INTO npc_frames (actor_id, stage_id, actor_index, frame_id, width, height, image_path) VALUES ($1, $2, $3, $4, $5, $6, $7)",
            c.ActorId,
            c.StageId,
            c.ActorIndex,
            frame.FrameId,
            frame.Width,
            frame.Height,
            relativePath);
    }

    /// <summary>
    /// Inserts one row into <c>npc_anim_sets</c> with the five directional sequence indices.
    /// Every NPC animation set carries exactly five direction slots (South, West, North, East, unused).
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="c">The character the set belongs to; provides actor id, stage id, and actor index.</param>
    /// <param name="set">The animation set with set id and five directional sequence indices.</param>
    /// <exception cref="ArgumentException">The set does not contain exactly five sequence indices.</exception>
    private static void WriteAnimSet(DataPackWriteContext context, NpcExtractedCharacter c, NpcAnimSet set)
    {
        if (set.SequenceIndices.Count != 5)
        {
            throw new ArgumentException($"Animation set {set.SetId} must have exactly 5 directional sequence indices.", nameof(set));
        }

        Insert(
            context,
            "INSERT INTO npc_anim_sets (actor_id, stage_id, actor_index, set_id, seq0, seq1, seq2, seq3, seq4) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
            c.ActorId,
            c.StageId,
            c.ActorIndex,
            set.SetId,
            set.SequenceIndices[0],
            set.SequenceIndices[1],
            set.SequenceIndices[2],
            set.SequenceIndices[3],
            set.SequenceIndices[4]);
    }

    /// <summary>
    /// Inserts one row into <c>npc_sequences</c> and one row per animation record into
    /// <c>npc_sequence_records</c> for the given character sequence.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="c">The character the sequence belongs to; provides actor id, stage id, and actor index.</param>
    /// <param name="seq">The animation sequence with terminator type and ordered animation records.</param>
    private static void WriteSequence(DataPackWriteContext context, NpcExtractedCharacter c, NpcAnimSequence seq)
    {
        string terminator = seq.Terminator == NpcSequenceTerminator.Loop ? "loop" : "end";
        Insert(
            context,
            "INSERT INTO npc_sequences (actor_id, stage_id, actor_index, seq_index, terminator) VALUES ($1, $2, $3, $4, $5)",
            c.ActorId,
            c.StageId,
            c.ActorIndex,
            seq.SequenceIndex,
            terminator);
        for (int ordinal = 0; ordinal < seq.Records.Count; ordinal++)
        {
            NpcAnimRecord record = seq.Records[ordinal];
            Insert(
                context,
                "INSERT INTO npc_sequence_records (actor_id, stage_id, actor_index, seq_index, ordinal, duration, frame_id, offset_x, offset_y) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
                c.ActorId,
                c.StageId,
                c.ActorIndex,
                seq.SequenceIndex,
                ordinal,
                record.Duration,
                record.Frame,
                record.OffsetX,
                record.OffsetY);
        }
    }

    /// <summary>
    /// Serialises the NPC character manifest to JSON and writes it to
    /// <c>&lt;prefix&gt;/manifest.json</c> under the data pack's image root.
    /// </summary>
    /// <param name="context">The data pack write context providing image path resolution.</param>
    /// <param name="c">The extracted NPC character with all frames, sets, and sequences.</param>
    /// <param name="prefix">The relative subdirectory under the data pack root (e.g. <c>npc/S200/0</c>).</param>
    private static void WriteManifest(DataPackWriteContext context, NpcExtractedCharacter c, string prefix)
    {
        string relativePath = $"{prefix}/manifest.json";
        string path = context.GetImagePath(relativePath); // reuses GetImagePath for generic data paths
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var manifest = new
        {
            schema = c.ManifestSchema,
            actor_id = c.ActorId,
            stage_id = c.StageId,
            actor_index = c.ActorIndex,
            archive = new
            {
                disc_path = c.ArchiveDiscPath,
                sha256 = c.ArchiveSha256,
                bytes = c.ArchiveBytes,
                entry = c.ArchiveEntry,
            },
            frame_count = c.Frames.Count,
            sets = c.AnimSets.Select(s => new { set_id = s.SetId, sequences = s.SequenceIndices }).ToArray(),
            sequences = c.Sequences.Select(s => new
            {
                index = s.SequenceIndex,
                terminator = s.Terminator.ToString().ToLowerInvariant(),
                records = s.Records.Select(r => new { r.Duration, r.Frame, r.OffsetX, r.OffsetY }).ToArray(),
            }).ToArray(),
            frames = c.Frames.ToDictionary(f => f.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture), f => new { size = new[] { f.Width, f.Height } }),
        };
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(manifest, ManifestSerializerOptions));
    }

    /// <summary>
    /// Executes a parameterised INSERT command against the current pack transaction.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="sql">The parameterised SQL text using positional parameters ($1, $2, …).</param>
    /// <param name="parameters">The parameter values in positional order.</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] parameters)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int i = 0; i < parameters.Length; i++)
        {
            command.Parameters.AddWithValue($"${i + 1}", parameters[i]);
        }

        command.ExecuteNonQuery();
    }
}
