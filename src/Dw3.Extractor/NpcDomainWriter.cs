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
    /// <summary>Writes the disc-derived actor placement manifest and normalized SQLite rows.</summary>
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

    private static void InsertCharacter(DataPackWriteContext context, NpcExtractedCharacter c, string prefix)
    {
        _ = prefix;
        Insert(
            context,
            """
            INSERT INTO npc_characters
                (actor_id, stage_id, actor_index, manifest_schema,
                 archive_disc_path, archive_sha256, archive_bytes, archive_entry, frame_count)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """,
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
            frames = c.Frames.ToDictionary(f => f.FrameId.ToString(), f => new { size = new[] { f.Width, f.Height } }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

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
