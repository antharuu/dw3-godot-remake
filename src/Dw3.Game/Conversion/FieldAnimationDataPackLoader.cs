using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>
/// Runtime reader for the field animation domains of the active data pack: the exit cue and
/// connection indicator (FIELDCOM sprites), the climb and drop player action archives, and the
/// animated map objects of each stage. Every value is read from SQLite and every image is a
/// pack-local PNG; a missing or malformed record raises <see cref="CacheResourceException"/>.
/// Complex parsing is delegated to <see cref="FieldAnimationParser"/>.
/// </summary>
public sealed class FieldAnimationDataPackLoader : IDisposable
{
    private const int CueFieldRows = 5;

    // Frames the exit cue draws: the pop sequence 0x32..0x37 (0x32, 0x35 and 0x37 are its
    // first, open and last frames), the eight direction arrows 0x0C..0x13 and the 0x45/0x46 pair.
    private static readonly int[] RequiredCueFrames = [0x32, 0x35, 0x37, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x45, 0x46];

    private static readonly int[] RequiredDropSets = [0x16, 0x17, 0x18, 0x19];

    private readonly DataPackReader _pack;

    private readonly SqliteConnection _database;

    /// <summary>Initializes a new instance of the <see cref="FieldAnimationDataPackLoader"/> class bound to the given data pack.</summary>
    /// <param name="pack">The validated data pack reader.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> is null.</exception>
    public FieldAnimationDataPackLoader(DataPackReader pack)
    {
        _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        _database = pack.OpenDatabase();
    }

    /// <summary>Loads the exit-cue sprite frames and direction row sequences.</summary>
    /// <returns>The fully decoded <see cref="ExitCue"/>.</returns>
    /// <exception cref="CacheResourceException">Required frames or direction rows are absent.</exception>
    public ExitCue LoadExitCue() => Read("exit cue", () =>
    {
        Dictionary<int, CueFrame> frames = LoadCueFrames();
        Dictionary<int, List<(int Frame, int Duration)>> rows = LoadCueRows();
        if (!RequiredCueFrames.All(frames.ContainsKey) || rows.Count != CueFieldRows)
        {
            throw Invalid("exit cue frames or direction rows incomplete");
        }

        return new ExitCue(frames, rows.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(int Frame, int Duration)>)pair.Value));
    });

    /// <summary>Loads the player connection-indicator frames and animation tables.</summary>
    /// <returns>The fully decoded <see cref="PlayerIconData"/>.</returns>
    /// <exception cref="CacheResourceException">Required icon frames or animation records are absent.</exception>
    public PlayerIconData LoadPlayerIcon() => Read("connection icon", () =>
        FieldAnimationParser.ParsePlayerIcon(_database, _pack));

    /// <summary>Loads the climb action animation sets from the data pack.</summary>
    /// <returns>The fully decoded <see cref="ClimbData"/>.</returns>
    /// <exception cref="CacheResourceException">The climb domain has no sequences in the data pack.</exception>
    public ClimbData LoadClimb() => LoadAction("climb", []);

    /// <summary>Loads the drop action animation sets from the data pack.</summary>
    /// <returns>The fully decoded <see cref="ClimbData"/>.</returns>
    /// <exception cref="CacheResourceException">Required drop sets are absent from the data pack.</exception>
    public ClimbData LoadDrop() => LoadAction("drop", RequiredDropSets);

    /// <summary>Returns the absolute path to the climb frame PNG for the given frame id.</summary>
    /// <param name="frame">The frame id to look up.</param>
    /// <returns>The absolute path to the climb frame PNG.</returns>
    /// <exception cref="CacheResourceException">The frame is absent from the data pack.</exception>
    public string ClimbFramePath(int frame) => ActionFramePath("climb", frame);

    /// <summary>Returns the absolute path to the drop frame PNG for the given frame id.</summary>
    /// <param name="frame">The frame id to look up.</param>
    /// <returns>The absolute path to the drop frame PNG.</returns>
    /// <exception cref="CacheResourceException">The frame is absent from the data pack.</exception>
    public string DropFramePath(int frame) => ActionFramePath("drop", frame);

    /// <summary>Loads all animated map objects and their frame cycles from the data pack.</summary>
    /// <returns>The fully decoded <see cref="WaterData"/>.</returns>
    /// <exception cref="CacheResourceException">A water object has no frames or references an absent zone.</exception>
    public WaterData LoadWater() => Read("map objects", () =>
        FieldAnimationParser.ParseWaterData(_database, _pack));

    /// <summary>Releases the underlying SQLite connection.</summary>
    public void Dispose() => _database.Dispose();

    /// <summary>Runs a load step and wraps SQLite and data conversion failures into a cache error, so every malformed field animation record surfaces as one error type.</summary>
    /// <typeparam name="T">Type of the loaded value.</typeparam>
    /// <param name="subject">Domain name used in the error message.</param>
    /// <param name="read">The load step.</param>
    /// <returns>The value returned by <paramref name="read"/>.</returns>
    /// <exception cref="CacheResourceException">The step threw a cache error, which is rethrown unchanged, or a SQLite, invalid operation or cast error, which is wrapped.</exception>
    private static T Read<T>(string subject, Func<T> read)
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

    /// <summary>Builds the error used when a field animation record breaks the pack contract.</summary>
    /// <param name="detail">Description of the defect.</param>
    /// <returns>An exception with code <c>CACHE_INVALID</c>, not yet thrown.</returns>
    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);

    /// <summary>Reads the exit cue sprites (domain <c>cue</c> of <c>fieldcom_frames</c>) and wraps each one with its frame id and draw offset.</summary>
    /// <returns>Cue frames keyed by frame id. Completeness is checked by the caller.</returns>
    /// <exception cref="CacheResourceException">A frame PNG is missing from the pack.</exception>
    private Dictionary<int, CueFrame> LoadCueFrames()
    {
        Dictionary<int, (string Path, int OffsetX, int OffsetY)> sprites = ReadFieldcomFrames("cue");
        return sprites.ToDictionary(pair => pair.Key, pair => new CueFrame(pair.Key, pair.Value.Path, pair.Value.OffsetX, pair.Value.OffsetY));
    }

    /// <summary>Reads the inner exit cue sequences of <c>cue_sequences</c>, one per field row, as (frame id, duration in source ticks) steps in disc order. The pop sequence is stored separately and is skipped here.</summary>
    /// <returns>Steps keyed by field row.</returns>
    /// <exception cref="CacheResourceException">An inner sequence row has a null field row.</exception>
    private Dictionary<int, List<(int Frame, int Duration)>> LoadCueRows()
    {
        var rows = new Dictionary<int, List<(int Frame, int Duration)>>();
        using SqliteCommand command = Command("SELECT field_row, frame_id, duration FROM cue_sequences WHERE kind='inner' ORDER BY field_row, ordinal");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                throw Invalid("exit cue inner sequence has no field row");
            }

            int row = reader.GetInt32(0);
            if (!rows.TryGetValue(row, out List<(int Frame, int Duration)>? steps))
            {
                steps = [];
                rows.Add(row, steps);
            }

            steps.Add((reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    /// <summary>Loads a player action archive (climb or drop) from <c>action_records</c> and <c>action_sets</c>, and checks that the sets the controller plays are present.</summary>
    /// <param name="domain">Action domain in the pack: <c>climb</c> or <c>drop</c>.</param>
    /// <param name="requiredSets">Set ids that must exist, for example 0x16..0x19 for the drop. Empty when any non-empty archive is accepted.</param>
    /// <returns>The action sequences keyed by set id.</returns>
    /// <exception cref="CacheResourceException">The archive has no sequence, misses a required set, or a set is malformed.</exception>
    private ClimbData LoadAction(string domain, IReadOnlyCollection<int> requiredSets) => Read($"{domain} actions", () =>
    {
        Dictionary<int, List<ClimbRecord>> records = LoadActionRecords(domain);
        Dictionary<int, ClimbSequence> sequences = BuildActionSequences(domain, records);
        if (sequences.Count == 0)
        {
            throw Invalid($"no {domain} sequences");
        }

        if (!requiredSets.All(sequences.ContainsKey))
        {
            throw Invalid($"{domain} required sets incomplete");
        }

        return new ClimbData(sequences);
    });

    /// <summary>Reads the frame records of every set of an action domain: frame id, duration in source ticks and sprite offset from the actor position, in disc order.</summary>
    /// <param name="domain">Action domain: <c>climb</c> or <c>drop</c>.</param>
    /// <returns>Records keyed by set id.</returns>
    private Dictionary<int, List<ClimbRecord>> LoadActionRecords(string domain)
    {
        var records = new Dictionary<int, List<ClimbRecord>>();
        using SqliteCommand command = Command("SELECT set_id, frame_id, duration, offset_x, offset_y FROM action_records WHERE domain=$domain ORDER BY set_id, ordinal", domain);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int set = reader.GetInt32(0);
            if (!records.TryGetValue(set, out List<ClimbRecord>? list))
            {
                list = [];
                records.Add(set, list);
            }

            list.Add(new ClimbRecord(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
        }

        return records;
    }

    /// <summary>Pairs each set listed in <c>action_sets</c> with its records and its terminator: <c>loop</c> sets repeat while the action lasts, <c>end</c> sets stop on their last frame.</summary>
    /// <param name="domain">Action domain: <c>climb</c> or <c>drop</c>.</param>
    /// <param name="records">Frame records keyed by set id.</param>
    /// <returns>Sequences keyed by set id.</returns>
    /// <exception cref="CacheResourceException">A set has a terminator other than <c>loop</c> or <c>end</c>, or has no record.</exception>
    private Dictionary<int, ClimbSequence> BuildActionSequences(string domain, Dictionary<int, List<ClimbRecord>> records)
    {
        var sequences = new Dictionary<int, ClimbSequence>();
        using SqliteCommand command = Command("SELECT set_id, terminator FROM action_sets WHERE domain=$domain ORDER BY set_id", domain);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int set = reader.GetInt32(0);
            string terminator = reader.GetString(1);
            if (terminator is not ("loop" or "end"))
            {
                throw Invalid($"{domain} set {set} has unknown terminator {terminator}");
            }

            if (!records.TryGetValue(set, out List<ClimbRecord>? list) || list.Count == 0)
            {
                throw Invalid($"{domain} set {set} has no records");
            }

            sequences.Add(set, new ClimbSequence(set, terminator == "loop", list));
        }

        return sequences;
    }

    /// <summary>Finds the PNG of one action frame in <c>action_frames</c> and resolves it inside the pack.</summary>
    /// <param name="domain">Action domain: <c>climb</c> or <c>drop</c>.</param>
    /// <param name="frame">Frame id referenced by an action record.</param>
    /// <returns>The absolute path of an existing PNG.</returns>
    /// <exception cref="CacheResourceException"><c>CACHE_RESOURCE_UNAVAILABLE</c> when the frame has no row or its file is absent, <c>CACHE_INVALID</c> on a SQLite error or a rejected path.</exception>
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

    /// <summary>Reads the sprites cut from the FIELDCOM archive for one domain (exit cue or connection indicator), with the draw offset of each frame in pixels.</summary>
    /// <param name="domain">Domain of <c>fieldcom_frames</c>, for example <c>cue</c>.</param>
    /// <returns>Absolute PNG path and offset keyed by frame id.</returns>
    /// <exception cref="CacheResourceException">A frame PNG is missing from the pack.</exception>
    private Dictionary<int, (string Path, int OffsetX, int OffsetY)> ReadFieldcomFrames(string domain)
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

    /// <summary>Creates a command on the loader's connection, binding <c>$domain</c> when a domain is given.</summary>
    /// <param name="sql">SQL statement. Only constant statements of this class are passed.</param>
    /// <param name="domain">Value bound to <c>$domain</c>, or <see langword="null"/> when the statement has no such parameter.</param>
    /// <returns>A new command the caller must dispose.</returns>
    private SqliteCommand Command(string sql, string? domain = null)
    {
        SqliteCommand command = _database.CreateCommand();
        command.CommandText = sql;
        if (domain is not null)
        {
            command.Parameters.AddWithValue("$domain", domain);
        }

        return command;
    }
}
