using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>
/// Runtime field-domain reader. It validates the active data pack through <see cref="DataPackReader"/>,
/// reads normalized registry and NPC rows from SQLite, and resolves only pack-local images.
/// </summary>
public sealed class FieldDataPackLoader : IDisposable
{
    private readonly DataPackReader _pack;
    private readonly SqliteConnection _database;
    private readonly IReadOnlyList<string> _zoneIds;

    public FieldDataPackLoader(DataPackReader pack)
    {
        _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        _database = pack.OpenDatabase();
        _zoneIds = ReadZoneIds();
        if (_zoneIds.Count == 0)
            throw new CacheResourceException("CACHE_INVALID", "data pack contains no loadable field zones");
    }

    /// <summary>Gets all loadable terrain stages in numeric order.</summary>
    public IReadOnlyList<string> ZoneIds => _zoneIds;

    /// <summary>
    /// Loads one field render contract after validating its registry row, manifest structure, layer
    /// images, collision maps, optional area map, and pack-local paths. Malformed storage is exposed
    /// as <see cref="CacheResourceException"/> rather than being replaced with fallback content.
    /// </summary>
    public Zone LoadZone(string zoneId)
    {
        int stage = ParseZoneId(zoneId);
        try
        {
            (int width, int height, string status) = ReadZoneHeader(stage);
            string directory = $"zones/{zoneId}";
            string manifestPath = RequirePackFile($"{directory}/manifest.json", $"zone={zoneId} manifest");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = document.RootElement;

            var layers = new[]
            {
                new Layer(RequirePackFile($"{directory}/depth-0.png", $"zone={zoneId} depth=0"), 7),
                new Layer(RequirePackFile($"{directory}/depth-1.png", $"zone={zoneId} depth=1"), 3),
                new Layer(RequirePackFile($"{directory}/depth-2.png", $"zone={zoneId} depth=2"), 11),
            };
            string floor = RequirePackFile($"{directory}/floor-cells.png", $"zone={zoneId} floor cells");
            string trigger = RequirePackFile($"{directory}/trigger-cells.png", $"zone={zoneId} trigger cells");
            string? areas = root.GetProperty("map_entries_source").GetProperty("areas_entry").ValueKind == JsonValueKind.Number
                ? RequirePackFile($"{directory}/areas-cells.png", $"zone={zoneId} area cells")
                : null;

            var climbs = new List<Climb>();
            var drops = new List<Drop>();
            int slotIndex = 0;
            foreach (JsonElement slot in root.GetProperty("slots").EnumerateArray())
            {
                int type = slot.GetProperty("type").GetInt32();
                int argument = slot.GetProperty("arg").GetInt32();
                if (type is 2 or 3)
                    climbs.Add(new Climb(slotIndex, type, slot.GetProperty("x").GetInt32(), slot.GetProperty("y").GetInt32(), (argument - 1) * 16));
                else if (type == 4)
                    drops.Add(new Drop(slotIndex, argument * 16));
                slotIndex++;
            }

            StreamModulate? modulate = ReadStreamModulate(root.GetProperty("render").GetProperty("stream_depth11_modulate"));
            string sourceHash = root.GetProperty("map_source").GetProperty("sha256").GetString()
                ?? throw new JsonException("map source hash missing");
            return new Zone(zoneId, width, height, layers, floor, trigger, ReadWarps(stage), climbs, sourceHash, modulate, status, drops, areas);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or SqliteException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"zone={zoneId} pack contract invalid: {error.Message}");
        }
    }

    public PlayerData LoadPlayer()
    {
        string path = RequirePackFile("player/manifest.json", "player manifest");
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement player = document.RootElement.GetProperty("player");
            JsonElement anchor = player.GetProperty("render_anchor").GetProperty("offset");
            var sequences = new List<PlayerSequence>();
            foreach (JsonElement set in player.GetProperty("sets").EnumerateArray())
                foreach (JsonElement sequence in set.GetProperty("sequences").EnumerateArray())
                {
                    var records = new List<AnimationRecord>();
                    foreach (JsonElement record in sequence.GetProperty("records").EnumerateArray())
                    {
                        JsonElement offset = record.GetProperty("offset");
                        JsonElement foot = record.GetProperty("foot_ground_contact");
                        records.Add(new AnimationRecord(
                            record.GetProperty("frame").GetInt32(),
                            record.GetProperty("duration").GetInt32(),
                            offset[0].GetInt32(), offset[1].GetInt32(),
                            (int)Math.Round(foot[0].GetDouble()), (int)Math.Round(foot[1].GetDouble())));
                    }
                    sequences.Add(new PlayerSequence(set.GetProperty("set").GetInt32(), sequence.GetProperty("direction").GetInt32(), records));
                }
            if (sequences.Count == 0) throw new JsonException("player manifest has no animation sequences");
            return new PlayerData(sequences, anchor[0].GetInt32(), anchor[1].GetInt32());
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"player pack contract invalid: {error.Message}");
        }
    }

    public string PlayerFramePath(int frame) =>
        RequirePackFile($"player/frames/frame-{frame:X2}.png", $"player frame={frame}");

    public NpcData LoadNpcs()
    {
        try
        {
            var zones = new Dictionary<string, List<NpcRecord>>(StringComparer.Ordinal);
            var occupants = new List<CollisionActor>();
            using SqliteCommand actors = _database.CreateCommand();
            actors.CommandText = """
                SELECT stage_id, actor_index, actor_id, x, y, direction, width
                FROM npc_actors
                WHERE snapshot_visible = 1 AND is_player_lead = 0 AND width IS NOT NULL
                ORDER BY stage_id, actor_index
                """;
            using SqliteDataReader rows = actors.ExecuteReader();
            while (rows.Read())
            {
                int stage = rows.GetInt32(0);
                int actorIndex = rows.GetInt32(1);
                int actorId = rows.GetInt32(2);
                int x = rows.GetInt32(3);
                int y = rows.GetInt32(4);
                int direction = rows.GetInt32(5);
                int width = rows.GetInt32(6);
                string zoneId = $"S{stage:D3}";
                string runtimeActorId = $"actor{actorIndex}";
                occupants.Add(new CollisionActor(zoneId, runtimeActorId, x, y, width));

                IReadOnlyList<NpcFrame>? idle = TryReadNpcIdle(stage, actorIndex, actorId, direction, out int spriteDirection, out bool flipH);
                if (idle is null) continue;
                if (!zones.TryGetValue(zoneId, out List<NpcRecord>? records))
                {
                    records = [];
                    zones.Add(zoneId, records);
                }
                records.Add(new NpcRecord(runtimeActorId, x, y, width, spriteDirection, flipH, idle));
            }
            if (zones.Count == 0)
                throw new CacheResourceException("CACHE_INVALID", "data pack contains no visible NPC render records");
            return new NpcData(zones.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<NpcRecord>)pair.Value), occupants);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is SqliteException or InvalidOperationException or IOException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"NPC pack contract invalid: {error.Message}");
        }
    }

    private IReadOnlyList<NpcFrame>? TryReadNpcIdle(int stage, int actorIndex, int actorId, int direction, out int spriteDirection, out bool flipH)
    {
        (spriteDirection, flipH) = ResolveNpcSpriteDirection(direction);
        using SqliteCommand sequence = _database.CreateCommand();
        sequence.CommandText = $"SELECT seq{spriteDirection} FROM npc_anim_sets WHERE actor_id=$actor AND stage_id=$stage AND actor_index=$index AND set_id=1";
        sequence.Parameters.AddWithValue("$actor", actorId);
        sequence.Parameters.AddWithValue("$stage", stage);
        sequence.Parameters.AddWithValue("$index", actorIndex);
        object? value = sequence.ExecuteScalar();
        if (value is null or DBNull) return null;
        int sequenceIndex = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        using SqliteCommand records = _database.CreateCommand();
        records.CommandText = """
            SELECT r.duration, r.frame_id, r.offset_x, r.offset_y, f.image_path
            FROM npc_sequence_records r
            JOIN npc_frames f ON f.actor_id=r.actor_id AND f.stage_id=r.stage_id
                AND f.actor_index=r.actor_index AND f.frame_id=r.frame_id
            WHERE r.actor_id=$actor AND r.stage_id=$stage AND r.actor_index=$index AND r.seq_index=$sequence
            ORDER BY r.ordinal
            """;
        records.Parameters.AddWithValue("$actor", actorId);
        records.Parameters.AddWithValue("$stage", stage);
        records.Parameters.AddWithValue("$index", actorIndex);
        records.Parameters.AddWithValue("$sequence", sequenceIndex);
        var idle = new List<NpcFrame>();
        using SqliteDataReader row = records.ExecuteReader();
        while (row.Read())
            idle.Add(new NpcFrame(RequirePackFile(row.GetString(4), $"NPC stage={stage} actor={actorIndex} frame={row.GetInt32(1)}"), row.GetInt32(2), row.GetInt32(3), row.GetInt32(0)));
        if (idle.Count == 0)
            throw new CacheResourceException("CACHE_INVALID", $"NPC stage={stage} actor={actorIndex} has an empty STAND loop");
        return idle;
    }

    private IReadOnlyList<string> ReadZoneIds()
    {
        var result = new List<string>();
        using SqliteCommand command = _database.CreateCommand();
        command.CommandText = """
            SELECT z.stage_id
            FROM zones z JOIN field_stages s ON s.stage_id=z.stage_id
            WHERE s.status='loadable'
            ORDER BY z.stage_id
            """;
        using SqliteDataReader rows = command.ExecuteReader();
        while (rows.Read()) result.Add($"S{rows.GetInt32(0):D3}");
        return result;
    }

    private (int Width, int Height, string Status) ReadZoneHeader(int stage)
    {
        using SqliteCommand command = _database.CreateCommand();
        command.CommandText = """
            SELECT z.width_pixels, z.height_pixels, s.status
            FROM zones z JOIN field_stages s ON s.stage_id=z.stage_id
            WHERE z.stage_id=$stage
            """;
        command.Parameters.AddWithValue("$stage", stage);
        using SqliteDataReader row = command.ExecuteReader();
        if (!row.Read())
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone=S{stage:D3} is not in the data pack");
        return (row.GetInt32(0), row.GetInt32(1), row.GetString(2));
    }

    private IReadOnlyList<Exit> ReadWarps(int stage)
    {
        var exits = new List<Exit>();
        using SqliteCommand command = _database.CreateCommand();
        command.CommandText = """
            SELECT slot_index, destination_stage_id, x, y, facing, condition, condition_status
            FROM field_warps WHERE source_stage_id=$stage ORDER BY slot_index
            """;
        command.Parameters.AddWithValue("$stage", stage);
        using SqliteDataReader rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (rows.IsDBNull(1)) continue;
            exits.Add(new Exit(rows.GetInt32(0), $"S{rows.GetInt32(1):D3}", rows.GetInt32(2), rows.GetInt32(3), rows.GetInt32(4), rows.GetInt32(5), rows.GetString(6)));
        }
        return exits;
    }

    private string RequirePackFile(string relativePath, string context) => DataPackFiles.Require(_pack, relativePath, context);

    private static StreamModulate? ReadStreamModulate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3)
            throw new JsonException("depth-11 stream modulate must be three channels or null");
        int[] channels = [.. element.EnumerateArray().Select(item => item.GetInt32())];
        if (channels.Any(channel => channel is < 0 or > 255))
            throw new JsonException("depth-11 stream channel outside byte range");
        return new StreamModulate(channels[0] / 128f, channels[1] / 128f, channels[2] / 128f);
    }

    private static int ParseZoneId(string zoneId)
    {
        if (zoneId.Length != 4 || zoneId[0] != 'S' || !int.TryParse(zoneId.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int stage))
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone={zoneId} is not a valid stage id");
        return stage;
    }

    private static (int Direction, bool FlipH) ResolveNpcSpriteDirection(int direction) => direction switch
    {
        0 => (0, false),
        1 => (1, false),
        2 => (2, false),
        3 => (3, false),
        4 => (4, false),
        5 => (3, true),
        6 => (2, true),
        7 => (1, true),
        _ => throw new CacheResourceException("CACHE_INVALID", $"invalid NPC direction {direction}"),
    };

    public void Dispose() => _database.Dispose();
}
