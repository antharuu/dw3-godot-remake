using System.Globalization;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>Static helpers that read the player connection icon and animated map object domains
/// from SQLite. These helpers are separated from <see cref="FieldAnimationDataPackLoader"/> to
/// keep the loader's cognitive complexity within the project limit.</summary>
internal static class FieldAnimationParser
{
    // Frame range for the connection indicator; 0x23..0x31 inclusive.
    private const int FirstIconFrame = 0x23;
    private const int LastIconFrame = 0x31;

    /// <summary>Reads all connection-indicator frames and animation tables from the data pack.</summary>
    /// <param name="database">The open SQLite connection to the data pack database.</param>
    /// <param name="pack">The data pack reader used to resolve image paths.</param>
    /// <returns>The fully decoded <see cref="PlayerIconData"/>.</returns>
    /// <exception cref="CacheResourceException">A required frame or animation record is absent or invalid.</exception>
    internal static PlayerIconData ParsePlayerIcon(SqliteConnection database, DataPackReader pack)
    {
        Dictionary<int, PlayerIconFrame> frames = LoadIconFrames(database, pack);
        for (int frame = FirstIconFrame; frame <= LastIconFrame; frame++)
        {
            if (!frames.ContainsKey(frame))
            {
                throw Invalid($"icon frame 0x{frame:X} missing");
            }
        }

        Dictionary<int, List<PlayerIconStep>> steps = LoadIconSteps(database);
        Dictionary<int, PlayerIconAnimation> animations = BuildIconAnimations(database, steps);
        if (!animations.ContainsKey(0))
        {
            throw Invalid("icon animation substate 0 missing");
        }

        return new PlayerIconData(frames, animations);
    }

    /// <summary>Reads all animated map object frames and zone records from the data pack.</summary>
    /// <param name="database">The open SQLite connection to the data pack database.</param>
    /// <param name="pack">The data pack reader used to resolve image paths.</param>
    /// <returns>The fully decoded <see cref="WaterData"/>.</returns>
    /// <exception cref="CacheResourceException">A water object has no frames or references an absent zone.</exception>
    internal static WaterData ParseWaterData(SqliteConnection database, DataPackReader pack)
    {
        Dictionary<(int Stage, int Object), List<WaterFrame>> frames = LoadWaterFrames(database, pack);
        Dictionary<string, List<WaterObject>> zones = LoadWaterObjects(database, frames);
        return new WaterData(zones.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WaterObject>)pair.Value, StringComparer.Ordinal));
    }

    private static Dictionary<int, PlayerIconFrame> LoadIconFrames(SqliteConnection database, DataPackReader pack)
    {
        var frames = new Dictionary<int, PlayerIconFrame>();
        using SqliteCommand command = CreateCommand(database, "SELECT frame_id, offset_x, offset_y, image_path FROM fieldcom_frames WHERE domain=$domain ORDER BY frame_id", "icon");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int frame = reader.GetInt32(0);
            frames.Add(frame, new PlayerIconFrame(frame, DataPackFiles.Require(pack, reader.GetString(3), $"icon frame={frame}"), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return frames;
    }

    private static Dictionary<int, List<PlayerIconStep>> LoadIconSteps(SqliteConnection database)
    {
        var steps = new Dictionary<int, List<PlayerIconStep>>();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT animation, frame_id, duration FROM icon_animation_steps ORDER BY animation, ordinal";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int animation = reader.GetInt32(0);
            if (!steps.TryGetValue(animation, out List<PlayerIconStep>? list))
            {
                list = [];
                steps.Add(animation, list);
            }

            list.Add(new PlayerIconStep(reader.GetInt32(1), reader.GetInt32(2)));
        }

        return steps;
    }

    private static Dictionary<int, PlayerIconAnimation> BuildIconAnimations(SqliteConnection database, Dictionary<int, List<PlayerIconStep>> steps)
    {
        var animations = new Dictionary<int, PlayerIconAnimation>();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT animation, loop_target FROM icon_animations ORDER BY animation";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int substate = reader.GetInt32(0);
            int loopTarget = reader.GetInt32(1);
            ValidateIconAnimation(substate, loopTarget, steps);
            animations.Add(substate, new PlayerIconAnimation(substate, steps[substate], loopTarget));
        }

        return animations;
    }

    private static void ValidateIconAnimation(int substate, int loopTarget, Dictionary<int, List<PlayerIconStep>> steps)
    {
        if (!steps.TryGetValue(substate, out List<PlayerIconStep>? list) || list.Count == 0)
        {
            throw Invalid($"icon animation substate {substate} is empty");
        }

        if (list.Any(step => step.Frame < 0 || step.Duration < 0))
        {
            throw Invalid($"icon animation substate {substate} has a negative step");
        }

        if (loopTarget < 0 || loopTarget >= list.Count)
        {
            throw Invalid($"icon animation substate {substate} loop target {loopTarget} out of range");
        }
    }

    private static Dictionary<(int Stage, int Object), List<WaterFrame>> LoadWaterFrames(SqliteConnection database, DataPackReader pack)
    {
        var frames = new Dictionary<(int Stage, int Object), List<WaterFrame>>();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = """
            SELECT stage_id, object_index, offset_x, offset_y, abr, opaque_image, stp_image
            FROM water_object_frames ORDER BY stage_id, object_index, ordinal
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = (reader.GetInt32(0), reader.GetInt32(1));
            string zone = ZoneId(key.Item1);
            if (!frames.TryGetValue(key, out List<WaterFrame>? list))
            {
                list = [];
                frames.Add(key, list);
            }

            list.Add(new WaterFrame(
                DataPackFiles.Require(pack, reader.GetString(5), $"water opaque zone={zone}"),
                DataPackFiles.Require(pack, reader.GetString(6), $"water STP zone={zone}"),
                reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }

        return frames;
    }

    private static Dictionary<string, List<WaterObject>> LoadWaterObjects(
        SqliteConnection database,
        Dictionary<(int Stage, int Object), List<WaterFrame>> frames)
    {
        var zones = LoadWaterZones(database);
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = """
            SELECT stage_id, object_index, margin, depth, cycle, cycle_first, cycle_last, cycle_delay,
                   frame, clut_row, cycle_time, x, y, sort_y
            FROM water_objects ORDER BY stage_id, object_index
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            AppendWaterObject(reader, frames, zones);
        }

        return zones;
    }

    private static Dictionary<string, List<WaterObject>> LoadWaterZones(SqliteConnection database)
    {
        var zones = new Dictionary<string, List<WaterObject>>(StringComparer.Ordinal);
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT stage_id FROM water_zones ORDER BY stage_id";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            zones.Add(ZoneId(reader.GetInt32(0)), []);
        }

        return zones;
    }

    private static void AppendWaterObject(
        SqliteDataReader reader,
        Dictionary<(int Stage, int Object), List<WaterFrame>> frames,
        Dictionary<string, List<WaterObject>> zones)
    {
        int stage = reader.GetInt32(0);
        int index = reader.GetInt32(1);
        if (!frames.TryGetValue((stage, index), out List<WaterFrame>? list) || list.Count == 0)
        {
            throw Invalid($"water object zone={ZoneId(stage)} index={index} has no frames");
        }

        if (!zones.TryGetValue(ZoneId(stage), out List<WaterObject>? objects))
        {
            throw Invalid($"water object zone={ZoneId(stage)} has no zone record");
        }

        objects.Add(new WaterObject(index, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
            reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10),
            reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), list));
    }

    private static SqliteCommand CreateCommand(SqliteConnection database, string sql, string domain)
    {
        SqliteCommand command = database.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$domain", domain);
        return command;
    }

    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);

    private static string ZoneId(int stage) => $"S{stage.ToString("D3", CultureInfo.InvariantCulture)}";
}
