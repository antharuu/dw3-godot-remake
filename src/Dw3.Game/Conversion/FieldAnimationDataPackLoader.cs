using System.Globalization;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>
/// Runtime reader for the field animation domains of the active data pack: the exit cue and
/// connection indicator (FIELDCOM sprites), the climb and drop player action archives, and the
/// animated map objects of each stage. Every value is read from SQLite and every image is a
/// pack-local PNG; a missing or malformed record raises <see cref="CacheResourceException"/>.
/// </summary>
public sealed class FieldAnimationDataPackLoader : IDisposable
{
    // Frames the exit cue draws: the pop sequence 0x32..0x37 (0x32, 0x35 and 0x37 are its
    // first, open and last frames), the eight direction arrows 0x0C..0x13 and the 0x45/0x46 pair.
    private static readonly int[] RequiredCueFrames = [0x32, 0x35, 0x37, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x45, 0x46];
    private const int CueFieldRows = 5;
    private const int FirstIconFrame = 0x23;
    private const int LastIconFrame = 0x31;
    private static readonly int[] RequiredDropSets = [0x16, 0x17, 0x18, 0x19];

    private readonly DataPackReader _pack;
    private readonly SqliteConnection _database;

    public FieldAnimationDataPackLoader(DataPackReader pack)
    {
        _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        _database = pack.OpenDatabase();
    }

    public ExitCue LoadExitCue() => Read("exit cue", () =>
    {
        IReadOnlyDictionary<int, (string Path, int OffsetX, int OffsetY)> sprites = ReadFieldcomFrames("cue");
        var frames = sprites.ToDictionary(pair => pair.Key, pair => new CueFrame(pair.Key, pair.Value.Path, pair.Value.OffsetX, pair.Value.OffsetY));
        var rows = new Dictionary<int, List<(int Frame, int Duration)>>();
        using SqliteCommand command = Command("SELECT field_row, frame_id, duration FROM cue_sequences WHERE kind='inner' ORDER BY field_row, ordinal");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0)) throw Invalid("exit cue inner sequence has no field row");
            int row = reader.GetInt32(0);
            if (!rows.TryGetValue(row, out List<(int Frame, int Duration)>? steps)) rows.Add(row, steps = []);
            steps.Add((reader.GetInt32(1), reader.GetInt32(2)));
        }
        if (!RequiredCueFrames.All(frames.ContainsKey) || rows.Count != CueFieldRows)
            throw Invalid("exit cue frames or direction rows incomplete");
        return new ExitCue(frames, rows.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(int Frame, int Duration)>)pair.Value));
    });

    public PlayerIconData LoadPlayerIcon() => Read("connection icon", () =>
    {
        IReadOnlyDictionary<int, (string Path, int OffsetX, int OffsetY)> sprites = ReadFieldcomFrames("icon");
        var frames = sprites.ToDictionary(pair => pair.Key, pair => new PlayerIconFrame(pair.Key, pair.Value.Path, pair.Value.OffsetX, pair.Value.OffsetY));
        for (int frame = FirstIconFrame; frame <= LastIconFrame; frame++)
            if (!frames.ContainsKey(frame)) throw Invalid($"icon frame 0x{frame:X} missing");

        var steps = new Dictionary<int, List<PlayerIconStep>>();
        using (SqliteCommand command = Command("SELECT animation, frame_id, duration FROM icon_animation_steps ORDER BY animation, ordinal"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int animation = reader.GetInt32(0);
                if (!steps.TryGetValue(animation, out List<PlayerIconStep>? list)) steps.Add(animation, list = []);
                list.Add(new PlayerIconStep(reader.GetInt32(1), reader.GetInt32(2)));
            }

        var animations = new Dictionary<int, PlayerIconAnimation>();
        using (SqliteCommand command = Command("SELECT animation, loop_target FROM icon_animations ORDER BY animation"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int substate = reader.GetInt32(0);
                int loopTarget = reader.GetInt32(1);
                if (!steps.TryGetValue(substate, out List<PlayerIconStep>? list) || list.Count == 0)
                    throw Invalid($"icon animation substate {substate} is empty");
                if (list.Any(step => step.Frame < 0 || step.Duration < 0))
                    throw Invalid($"icon animation substate {substate} has a negative step");
                if (loopTarget < 0 || loopTarget >= list.Count)
                    throw Invalid($"icon animation substate {substate} loop target {loopTarget} out of range");
                animations.Add(substate, new PlayerIconAnimation(substate, list, loopTarget));
            }
        if (!animations.ContainsKey(0)) throw Invalid("icon animation substate 0 missing");
        return new PlayerIconData(frames, animations);
    });

    public ClimbData LoadClimb() => LoadAction("climb", []);

    public ClimbData LoadDrop() => LoadAction("drop", RequiredDropSets);

    public string ClimbFramePath(int frame) => ActionFramePath("climb", frame);

    public string DropFramePath(int frame) => ActionFramePath("drop", frame);

    public WaterData LoadWater() => Read("map objects", () =>
    {
        var frames = new Dictionary<(int Stage, int Object), List<WaterFrame>>();
        using (SqliteCommand command = Command("""
            SELECT stage_id, object_index, offset_x, offset_y, abr, opaque_image, stp_image
            FROM water_object_frames ORDER BY stage_id, object_index, ordinal
            """))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1));
                string zone = ZoneId(key.Item1);
                if (!frames.TryGetValue(key, out List<WaterFrame>? list)) frames.Add(key, list = []);
                list.Add(new WaterFrame(
                    DataPackFiles.Require(_pack, reader.GetString(5), $"water opaque zone={zone}"),
                    DataPackFiles.Require(_pack, reader.GetString(6), $"water STP zone={zone}"),
                    reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
            }

        var zones = new Dictionary<string, List<WaterObject>>(StringComparer.Ordinal);
        using (SqliteCommand command = Command("SELECT stage_id FROM water_zones ORDER BY stage_id"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read()) zones.Add(ZoneId(reader.GetInt32(0)), []);
        using (SqliteCommand command = Command("""
            SELECT stage_id, object_index, margin, depth, cycle, cycle_first, cycle_last, cycle_delay,
                   frame, clut_row, cycle_time, x, y, sort_y
            FROM water_objects ORDER BY stage_id, object_index
            """))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int stage = reader.GetInt32(0);
                int index = reader.GetInt32(1);
                if (!frames.TryGetValue((stage, index), out List<WaterFrame>? list) || list.Count == 0)
                    throw Invalid($"water object zone={ZoneId(stage)} index={index} has no frames");
                if (!zones.TryGetValue(ZoneId(stage), out List<WaterObject>? objects))
                    throw Invalid($"water object zone={ZoneId(stage)} has no zone record");
                objects.Add(new WaterObject(index, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                    reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10),
                    reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), list));
            }
        return new WaterData(zones.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WaterObject>)pair.Value, StringComparer.Ordinal));
    });

    public void Dispose() => _database.Dispose();

    private ClimbData LoadAction(string domain, IReadOnlyCollection<int> requiredSets) => Read($"{domain} actions", () =>
    {
        var records = new Dictionary<int, List<ClimbRecord>>();
        using (SqliteCommand command = Command("SELECT set_id, frame_id, duration, offset_x, offset_y FROM action_records WHERE domain=$domain ORDER BY set_id, ordinal", domain))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int set = reader.GetInt32(0);
                if (!records.TryGetValue(set, out List<ClimbRecord>? list)) records.Add(set, list = []);
                list.Add(new ClimbRecord(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
            }

        var sequences = new Dictionary<int, ClimbSequence>();
        using (SqliteCommand command = Command("SELECT set_id, terminator FROM action_sets WHERE domain=$domain ORDER BY set_id", domain))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int set = reader.GetInt32(0);
                string terminator = reader.GetString(1);
                if (terminator is not ("loop" or "end")) throw Invalid($"{domain} set {set} has unknown terminator {terminator}");
                if (!records.TryGetValue(set, out List<ClimbRecord>? list) || list.Count == 0)
                    throw Invalid($"{domain} set {set} has no records");
                sequences.Add(set, new ClimbSequence(set, terminator == "loop", list));
            }
        if (sequences.Count == 0) throw Invalid($"no {domain} sequences");
        if (!requiredSets.All(sequences.ContainsKey)) throw Invalid($"{domain} required sets incomplete");
        return new ClimbData(sequences);
    });

    private string ActionFramePath(string domain, int frame)
    {
        string context = $"{domain} frame={frame}";
        try
        {
            using SqliteCommand command = Command("SELECT image_path FROM action_frames WHERE domain=$domain AND frame_id=$frame", domain);
            command.Parameters.AddWithValue("$frame", frame);
            return command.ExecuteScalar() is string relative
                ? DataPackFiles.Require(_pack, relative, context)
                : throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"{context} is not in the data pack");
        }
        catch (SqliteException error)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{context} pack contract invalid: {error.Message}");
        }
    }

    private IReadOnlyDictionary<int, (string Path, int OffsetX, int OffsetY)> ReadFieldcomFrames(string domain)
    {
        var frames = new Dictionary<int, (string Path, int OffsetX, int OffsetY)>();
        using SqliteCommand command = Command("SELECT frame_id, offset_x, offset_y, image_path FROM fieldcom_frames WHERE domain=$domain ORDER BY frame_id", domain);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int frame = reader.GetInt32(0);
            frames.Add(frame, (DataPackFiles.Require(_pack, reader.GetString(3), $"{domain} frame={frame}"), reader.GetInt32(1), reader.GetInt32(2)));
        }
        return frames;
    }

    private T Read<T>(string subject, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is SqliteException or InvalidOperationException or InvalidCastException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{subject} pack contract invalid: {error.Message}");
        }
    }

    private SqliteCommand Command(string sql, string? domain = null)
    {
        SqliteCommand command = _database.CreateCommand();
        command.CommandText = sql;
        if (domain is not null) command.Parameters.AddWithValue("$domain", domain);
        return command;
    }

    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);

    private static string ZoneId(int stage) => $"S{stage.ToString("D3", CultureInfo.InvariantCulture)}";
}
