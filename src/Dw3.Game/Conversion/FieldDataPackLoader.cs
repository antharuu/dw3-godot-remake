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
    /// <summary>
    /// The standing-sequence query of each sprite direction 0..4: <c>npc_anim_sets</c> stores one
    /// sequence index column per direction (<c>seq0</c> to <c>seq4</c>), and set 1 is the standing set.
    /// The queries are spelled out because a column name cannot be a SQL parameter.
    /// </summary>
    private static readonly string[] NpcIdleSequenceQueries =
    [
        "SELECT seq0 FROM npc_anim_sets WHERE actor_id=$actor AND stage_id=$stage AND actor_index=$index AND set_id=1",
        "SELECT seq1 FROM npc_anim_sets WHERE actor_id=$actor AND stage_id=$stage AND actor_index=$index AND set_id=1",
        "SELECT seq2 FROM npc_anim_sets WHERE actor_id=$actor AND stage_id=$stage AND actor_index=$index AND set_id=1",
        "SELECT seq3 FROM npc_anim_sets WHERE actor_id=$actor AND stage_id=$stage AND actor_index=$index AND set_id=1",
        "SELECT seq4 FROM npc_anim_sets WHERE actor_id=$actor AND stage_id=$stage AND actor_index=$index AND set_id=1",
    ];

    private readonly DataPackReader _pack;

    private readonly SqliteConnection _database;

    private readonly List<string> _zoneIds;

    /// <summary>Initializes a new instance of the <see cref="FieldDataPackLoader"/> class bound to the given data pack.</summary>
    /// <param name="pack">The validated data pack reader.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> is null.</exception>
    /// <exception cref="CacheResourceException">The data pack contains no loadable field zones.</exception>
    public FieldDataPackLoader(DataPackReader pack)
    {
        _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        _database = pack.OpenDatabase();
        _zoneIds = ReadZoneIds();
        if (_zoneIds.Count == 0)
        {
            throw new CacheResourceException("CACHE_INVALID", "data pack contains no loadable field zones");
        }
    }

    /// <summary>Gets all loadable terrain stages in numeric order.</summary>
    public IReadOnlyList<string> ZoneIds => _zoneIds;

    /// <summary>
    /// Loads one field render contract after validating its registry row, manifest structure, layer
    /// images, collision maps, optional area map, and pack-local paths. Malformed storage is exposed
    /// as <see cref="CacheResourceException"/> rather than being replaced with fallback content.
    /// </summary>
    /// <param name="zoneId">The zone id string (e.g. "S200").</param>
    /// <returns>The fully loaded <see cref="Zone"/> contract.</returns>
    /// <exception cref="CacheResourceException">The zone is absent or its pack contract is violated.</exception>
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
                {
                    climbs.Add(new Climb(slotIndex, type, slot.GetProperty("x").GetInt32(), slot.GetProperty("y").GetInt32(), (argument - 1) * 16));
                }
                else if (type == 4)
                {
                    drops.Add(new Drop(slotIndex, argument * 16));
                }

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

    /// <summary>Loads the player animation manifest from the data pack.</summary>
    /// <returns>The fully loaded <see cref="PlayerData"/>.</returns>
    /// <exception cref="CacheResourceException">The player manifest is absent or its contract is violated.</exception>
    public PlayerData LoadPlayer()
    {
        string path = RequirePackFile("player/manifest.json", "player manifest");
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement player = document.RootElement.GetProperty("player");
            JsonElement anchor = player.GetProperty("render_anchor").GetProperty("offset");
            var sequences = ParsePlayerSequences(player);
            if (sequences.Count == 0)
            {
                throw new JsonException("player manifest has no animation sequences");
            }

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

    /// <summary>Returns the absolute path to the player frame PNG for the given frame id.</summary>
    /// <param name="frame">The frame id to look up.</param>
    /// <returns>The absolute path to the player frame PNG.</returns>
    /// <exception cref="CacheResourceException">The frame is absent from the data pack.</exception>
    public string PlayerFramePath(int frame) =>
        RequirePackFile($"player/frames/frame-{frame:X2}.png", $"player frame={frame}");

    /// <summary>Loads all visible NPC render records and occupancy actors from the data pack.</summary>
    /// <returns>The fully loaded <see cref="NpcData"/>.</returns>
    /// <exception cref="CacheResourceException">The data pack contains no visible NPC records.</exception>
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

                List<NpcFrame>? idle = TryReadNpcIdle(stage, actorIndex, actorId, direction, out int spriteDirection, out bool flipH);
                if (idle is null)
                {
                    continue;
                }

                if (!zones.TryGetValue(zoneId, out List<NpcRecord>? records))
                {
                    records = [];
                    zones.Add(zoneId, records);
                }

                records.Add(new NpcRecord(runtimeActorId, x, y, width, spriteDirection, flipH, idle));
            }

            if (zones.Count == 0)
            {
                throw new CacheResourceException("CACHE_INVALID", "data pack contains no visible NPC render records");
            }

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

    /// <summary>Releases the underlying SQLite connection.</summary>
    public void Dispose() => _database.Dispose();

    /// <summary>Flattens the <c>sets</c> array of the player animation JSON into one list holding every (set, direction) sequence.</summary>
    /// <param name="player">Root element of the player animation JSON.</param>
    /// <returns>Sequences in file order, sets first, then directions inside each set.</returns>
    private static List<PlayerSequence> ParsePlayerSequences(JsonElement player)
    {
        var sequences = new List<PlayerSequence>();
        foreach (JsonElement set in player.GetProperty("sets").EnumerateArray())
        {
            foreach (JsonElement sequence in set.GetProperty("sequences").EnumerateArray())
            {
                sequences.Add(ParseOnePlayerSequence(set, sequence));
            }
        }

        return sequences;
    }

    /// <summary>Reads the records of one player sequence: frame id, duration in source ticks, sprite offset in pixels and the foot ground contact point, rounded to whole pixels because the extractor stores it as a float.</summary>
    /// <param name="set">The animation set object, which carries the set id.</param>
    /// <param name="sequence">One element of the set's <c>sequences</c> array, which carries the direction.</param>
    /// <returns>The sequence with its records in disc order.</returns>
    private static PlayerSequence ParseOnePlayerSequence(JsonElement set, JsonElement sequence)
    {
        var records = new List<AnimationRecord>();
        foreach (JsonElement record in sequence.GetProperty("records").EnumerateArray())
        {
            JsonElement offset = record.GetProperty("offset");
            JsonElement foot = record.GetProperty("foot_ground_contact");
            records.Add(new AnimationRecord(
                record.GetProperty("frame").GetInt32(),
                record.GetProperty("duration").GetInt32(),
                offset[0].GetInt32(),
                offset[1].GetInt32(),
                (int)Math.Round(foot[0].GetDouble()),
                (int)Math.Round(foot[1].GetDouble())));
        }

        return new PlayerSequence(set.GetProperty("set").GetInt32(), sequence.GetProperty("direction").GetInt32(), records);
    }

    /// <summary>Reads the color modulation of the depth-11 streamed layer. Channels are PSX vertex colors where 128 means unchanged, so each byte is divided by 128 (range 0..~2).</summary>
    /// <param name="element">A three-integer array, or JSON null when the layer is not modulated.</param>
    /// <returns>The modulation, or <see langword="null"/> for JSON null.</returns>
    /// <exception cref="JsonException">The value is neither null nor a three-element array, or a channel is outside 0..255.</exception>
    private static StreamModulate? ReadStreamModulate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3)
        {
            throw new JsonException("depth-11 stream modulate must be three channels or null");
        }

        int[] channels = [.. element.EnumerateArray().Select(item => item.GetInt32())];
        if (channels.Any(channel => channel is < 0 or > 255))
        {
            throw new JsonException("depth-11 stream channel outside byte range");
        }

        return new StreamModulate(channels[0] / 128f, channels[1] / 128f, channels[2] / 128f);
    }

    /// <summary>Parses a zone name <c>Snnn</c> back into its stage id. Signs, spaces and other lengths are rejected.</summary>
    /// <param name="zoneId">Zone name, exactly four characters.</param>
    /// <returns>The stage id, 0..999.</returns>
    /// <exception cref="CacheResourceException">The name is not <c>S</c> followed by three digits.</exception>
    private static int ParseZoneId(string zoneId)
    {
        if (zoneId.Length != 4 || zoneId[0] != 'S' || !int.TryParse(zoneId.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int stage))
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone={zoneId} is not a valid stage id");
        }

        return stage;
    }

    /// <summary>Maps a facing direction 0..7 to one of the five sprite directions stored on the disc, mirroring 5, 6 and 7 from 3, 2 and 1.</summary>
    /// <param name="direction">Facing direction, 0..7.</param>
    /// <returns>The stored sprite direction (0..4) and whether to flip it horizontally.</returns>
    /// <exception cref="CacheResourceException">The direction is outside 0..7.</exception>
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

    /// <summary>Reads the standing (set 1) loop of an NPC for its placed direction. The disc stores five sprite directions (0..4); directions 5..7 reuse 3..1 mirrored horizontally.</summary>
    /// <param name="stage">Stage id of the NPC.</param>
    /// <param name="actorIndex">Index of the NPC in the stage's actor list.</param>
    /// <param name="actorId">Actor id that selects the NPC's sprite archive.</param>
    /// <param name="direction">Placed facing direction, 0..7.</param>
    /// <param name="spriteDirection">Receives the stored sprite direction, 0..4.</param>
    /// <param name="flipH">Receives <see langword="true"/> when the sprite must be mirrored.</param>
    /// <returns>The loop frames with their PNG, offset and duration, or <see langword="null"/> when the NPC has no standing set for that direction.</returns>
    /// <exception cref="CacheResourceException">The direction is outside 0..7, the standing set exists but has no record, or a frame PNG is missing.</exception>
    private List<NpcFrame>? TryReadNpcIdle(int stage, int actorIndex, int actorId, int direction, out int spriteDirection, out bool flipH)
    {
        (spriteDirection, flipH) = ResolveNpcSpriteDirection(direction);
        using SqliteCommand sequence = _database.CreateCommand();
        sequence.CommandText = NpcIdleSequenceQueries[spriteDirection];
        sequence.Parameters.AddWithValue("$actor", actorId);
        sequence.Parameters.AddWithValue("$stage", stage);
        sequence.Parameters.AddWithValue("$index", actorIndex);
        object? value = sequence.ExecuteScalar();
        if (value is null or DBNull)
        {
            return null;
        }

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
        {
            idle.Add(new NpcFrame(RequirePackFile(row.GetString(4), $"NPC stage={stage} actor={actorIndex} frame={row.GetInt32(1)}"), row.GetInt32(2), row.GetInt32(3), row.GetInt32(0)));
        }

        if (idle.Count == 0)
        {
            throw new CacheResourceException("CACHE_INVALID", $"NPC stage={stage} actor={actorIndex} has an empty STAND loop");
        }

        return idle;
    }

    /// <summary>Lists the zones whose stage the extractor marked <c>loadable</c>. Stages that failed to decode stay in the pack for diagnostics but are not offered to the runtime.</summary>
    /// <returns>Zone names <c>Snnn</c> in stage id order.</returns>
    private List<string> ReadZoneIds()
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
        while (rows.Read())
        {
            result.Add($"S{rows.GetInt32(0):D3}");
        }

        return result;
    }

    /// <summary>Reads the map size in pixels and the extraction status of one stage.</summary>
    /// <param name="stage">Stage id.</param>
    /// <returns>Width and height of the background in pixels, and the status written by the extractor.</returns>
    /// <exception cref="CacheResourceException">The stage has no zone row.</exception>
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
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"zone=S{stage:D3} is not in the data pack");
        }

        return (row.GetInt32(0), row.GetInt32(1), row.GetString(2));
    }

    /// <summary>Reads the warp slots of a stage from <c>field_warps</c>: destination, trigger tile, arrival facing, gate condition and whether that condition is resolved. Slots with no destination stage are unused on the disc and are skipped.</summary>
    /// <param name="stage">Source stage id.</param>
    /// <returns>Exits in slot order.</returns>
    private List<FieldExit> ReadWarps(int stage)
    {
        var exits = new List<FieldExit>();
        using SqliteCommand command = _database.CreateCommand();
        command.CommandText = """
            SELECT slot_index, destination_stage_id, x, y, facing, condition, condition_status
            FROM field_warps WHERE source_stage_id=$stage ORDER BY slot_index
            """;
        command.Parameters.AddWithValue("$stage", stage);
        using SqliteDataReader rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (rows.IsDBNull(1))
            {
                continue;
            }

            exits.Add(new FieldExit(rows.GetInt32(0), $"S{rows.GetInt32(1):D3}", rows.GetInt32(2), rows.GetInt32(3), rows.GetInt32(4), rows.GetInt32(5), rows.GetString(6)));
        }

        return exits;
    }

    /// <summary>Resolves a pack-relative path through <see cref="DataPackFiles.Require"/> with this loader's pack.</summary>
    /// <param name="relativePath">Path relative to the pack root.</param>
    /// <param name="context">Description of the resource, used in the error message.</param>
    /// <returns>An absolute path to an existing file.</returns>
    /// <exception cref="CacheResourceException">The path is rejected or the file is absent.</exception>
    private string RequirePackFile(string relativePath, string context) => DataPackFiles.Require(_pack, relativePath, context);
}
