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

    /// <summary>Reads the connection indicator sprites (domain <c>icon</c> of <c>fieldcom_frames</c>) with their draw offset in pixels.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="pack">Pack used to resolve and check each PNG path.</param>
    /// <returns>Icon frames keyed by frame id. The 0x23..0x31 range is checked by the caller.</returns>
    /// <exception cref="CacheResourceException">A frame PNG is missing from the pack.</exception>
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

    /// <summary>Reads <c>icon_animation_steps</c>: for each indicator animation (one per connection substate), its (frame id, duration in source ticks) steps in disc order.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <returns>Steps keyed by animation id, unvalidated.</returns>
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

    /// <summary>Reads <c>icon_animations</c> and pairs each animation with its steps and the step index it loops back to after its last step.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="steps">Steps keyed by animation id.</param>
    /// <returns>Validated animations keyed by substate.</returns>
    /// <exception cref="CacheResourceException">An animation fails <see cref="ValidateIconAnimation"/>.</exception>
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

    /// <summary>Checks that an indicator animation can be played without bounds errors: at least one step, no negative frame or duration, and a loop target inside the step list.</summary>
    /// <param name="substate">Animation id, equal to the connection substate that plays it.</param>
    /// <param name="loopTarget">Step index replayed after the last step. Valid range is 0..step count - 1.</param>
    /// <param name="steps">Steps keyed by animation id.</param>
    /// <exception cref="CacheResourceException">The animation has no step, a negative step, or an out-of-range loop target.</exception>
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

    /// <summary>Reads <c>water_object_frames</c>: each frame of an animated map object has two PNGs, one for the opaque texels and one for the semi-transparent (STP) texels, plus a pixel offset and the PSX blend mode (<c>abr</c>, 0..3) used for the STP image.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="pack">Pack used to resolve and check each PNG path.</param>
    /// <returns>Frames keyed by stage id and object index, in disc order.</returns>
    /// <exception cref="CacheResourceException">An opaque or STP PNG is missing from the pack.</exception>
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
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return frames;
    }

    /// <summary>Reads <c>water_objects</c> into the zones listed in <c>water_zones</c>. Each object carries its draw margin, depth, color cycle range and delay, start frame, CLUT row, cycle time, position and sort line.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="frames">Frames keyed by stage id and object index.</param>
    /// <returns>Objects grouped by zone name. A listed zone without objects keeps an empty list.</returns>
    /// <exception cref="CacheResourceException">An object has no frame or belongs to a stage absent from <c>water_zones</c>.</exception>
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

    /// <summary>Reads the stages that have animated map objects from <c>water_zones</c>.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <returns>One empty object list per zone name <c>Snnn</c>, to be filled by the caller.</returns>
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

    /// <summary>Converts the current <c>water_objects</c> row into an object with its frames and appends it to its zone.</summary>
    /// <param name="reader">Reader positioned on a <c>water_objects</c> row, columns in the order of the query in <see cref="LoadWaterObjects"/>.</param>
    /// <param name="frames">Frames keyed by stage id and object index.</param>
    /// <param name="zones">Object lists keyed by zone name.</param>
    /// <exception cref="CacheResourceException">The object has no frame or its zone is not listed.</exception>
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

        objects.Add(new WaterObject(
            index,
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            list));
    }

    /// <summary>Creates a command on the given connection with <c>$domain</c> bound.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="sql">SQL statement using a <c>$domain</c> parameter.</param>
    /// <param name="domain">Value bound to <c>$domain</c>.</param>
    /// <returns>A new command the caller must dispose.</returns>
    private static SqliteCommand CreateCommand(SqliteConnection database, string sql, string domain)
    {
        SqliteCommand command = database.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$domain", domain);
        return command;
    }

    /// <summary>Builds the error used when an indicator or map object record breaks the pack contract.</summary>
    /// <param name="detail">Description of the defect.</param>
    /// <returns>An exception with code <c>CACHE_INVALID</c>, not yet thrown.</returns>
    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);

    /// <summary>Formats a stage id as the zone name used across the runtime.</summary>
    /// <param name="stage">Stage id, 0..999.</param>
    /// <returns><c>S</c> followed by the id on three digits, for example <c>S200</c>.</returns>
    private static string ZoneId(int stage) => $"S{stage.ToString("D3", CultureInfo.InvariantCulture)}";
}
