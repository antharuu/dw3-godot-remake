using System.Globalization;
using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>
/// Runtime reader for the dialogue domains of the active data pack: the per-stage talk tables,
/// the decoded FSTALK text streams, the three font styles and the talk box. Numeric records are
/// read from SQLite, text tokens from the pack's text files and images from pack-local PNGs. A
/// missing or malformed record raises <see cref="CacheResourceException"/>; no text, glyph, frame
/// or layout is ever substituted.
/// </summary>
public sealed class DialogueDataPackLoader : IDisposable
{
    // Line height of each font style; style 1 is the field dialogue style.
    private static readonly IReadOnlyDictionary<int, int> StyleLineHeights = new Dictionary<int, int> { [1] = 14, [2] = 11, [3] = 9 };

    private const int DialogueStyle = 1;

    // Frames the talk box draws: two edges, the four corners and the "next" arrow.
    private static readonly int[] RequiredFrames = [0, 2, 3, 4, 5, 6, 7];

    private const int LayoutCount = 4;

    private readonly DataPackReader _pack;
    private readonly SqliteConnection _database;

    /// <summary>Initializes a new instance of the <see cref="DialogueDataPackLoader"/> class bound to the given data pack.</summary>
    /// <param name="pack">The validated data pack reader.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> is null.</exception>
    public DialogueDataPackLoader(DataPackReader pack)
    {
        _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        _database = pack.OpenDatabase();
    }

    /// <summary>Loads the full dialogue contract: zones, text streams and the font.</summary>
    /// <returns>The fully loaded <see cref="DialogueData"/>.</returns>
    /// <exception cref="CacheResourceException">The data pack is missing required records.</exception>
    public DialogueData LoadDialogue() => Read("dialogue", () =>
    {
        Dictionary<string, ZoneDialogue> zones = ReadZones();
        if (zones.Count == 0)
        {
            throw Invalid("dialogue pack has no zones");
        }

        Dictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>> texts = ReadTexts();
        foreach ((string zone, ZoneDialogue dialogue) in zones)
        {
            if (!texts.ContainsKey(dialogue.TalkFile))
            {
                throw Invalid($"dialogue zone={zone} uses absent text file {dialogue.TalkFile}");
            }
        }

        return new DialogueData(zones, texts, ReadFont());
    });

    /// <summary>Loads the talk-box contract: seven frame PNGs, four layouts and the panel sprite.</summary>
    /// <returns>The fully loaded <see cref="DialogueFrameData"/>.</returns>
    /// <exception cref="CacheResourceException">The data pack is missing required talk-box records.</exception>
    public DialogueFrameData LoadDialogueFrame() => Read("talk box", () =>
    {
        Dictionary<int, DialogueFrame> frames = ReadTalkBoxFrames();
        PanelSpec panel = ReadTalkBoxPanel();
        List<TalkBoxLayout> layouts = ReadTalkBoxLayouts();
        return new DialogueFrameData(frames, layouts, panel);
    });

    /// <summary>Releases the underlying SQLite connection.</summary>
    public void Dispose() => _database.Dispose();

    /// <summary>Reads every stage's talk table from the pack database through <see cref="DialogueZoneReader"/>.</summary>
    /// <returns>Zone dialogue keyed by stage name. May be empty, which the caller rejects.</returns>
    private Dictionary<string, ZoneDialogue> ReadZones() => DialogueZoneReader.ReadZones(_database);

    /// <summary>Reads the talk box frame sprites from <c>talk_box_frames</c> (size in pixels, CLUT frame count, semi-transparency flag, PNG path) and checks that the seven frames the box draws are all present.</summary>
    /// <returns>Frames keyed by frame id, containing at least ids 0 and 2..7.</returns>
    /// <exception cref="CacheResourceException">A required frame is absent or a frame PNG is missing from the pack.</exception>
    private Dictionary<int, DialogueFrame> ReadTalkBoxFrames()
    {
        var frames = new Dictionary<int, DialogueFrame>();
        using SqliteCommand command = Command("SELECT frame_id, width, height, clut_frames, semi, image_path FROM talk_box_frames ORDER BY frame_id");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            frames.Add(id, new DialogueFrame(
                id,
                DataPackFiles.Require(_pack, reader.GetString(5), $"talk-box frame={id}"),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4) != 0));
        }

        foreach (int required in RequiredFrames)
        {
            if (!frames.ContainsKey(required))
            {
                throw Invalid($"talk-box frame {required} missing");
            }
        }

        return frames;
    }

    /// <summary>Reads the single <c>talk_box_panel</c> row: the panel fill color (8-bit RGB), semi-transparency, texture page and CLUT of the source primitive, and the panel sprite PNG with its size.</summary>
    /// <returns>The panel description.</returns>
    /// <exception cref="CacheResourceException">The table has zero or several rows, or the panel PNG is missing.</exception>
    private PanelSpec ReadTalkBoxPanel()
    {
        PanelSpec? panel = null;
        using SqliteCommand command = Command("SELECT red, green, blue, semi, texture_page, clut, image_path, width, height FROM talk_box_panel");
        using SqliteDataReader reader = command.ExecuteReader();
        if (reader.Read())
        {
            panel = new PanelSpec(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3) != 0,
                reader.GetInt32(4),
                reader.GetInt32(5),
                DataPackFiles.Require(_pack, reader.GetString(6), "talk-box panel"),
                reader.GetInt32(7),
                reader.GetInt32(8));
        }

        if (panel is null || reader.Read())
        {
            throw Invalid("talk-box panel must have exactly one record");
        }

        return panel;
    }

    /// <summary>Reads the four talk box layouts from <c>talk_box_layouts</c>. Each layout gives, in screen pixels of the 320-wide original frame, the positions of the portrait sprites, the panel, the speaker name, the text origin and the "next" arrow.</summary>
    /// <returns>Exactly four layouts, list index equal to layout id.</returns>
    /// <exception cref="CacheResourceException">A layout id is skipped or the table does not hold exactly four layouts.</exception>
    private List<TalkBoxLayout> ReadTalkBoxLayouts()
    {
        var layouts = new List<TalkBoxLayout>();
        using SqliteCommand command = Command("""
            SELECT layout, sprite, sprite_x, sprite_y, sprite0_x, sprite0_y, panel_x, panel_y, sprite2_x, sprite2_y,
                   name_x, name_y, text_x, text_y, arrow_x, arrow_y
            FROM talk_box_layouts ORDER BY layout
            """);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetInt32(0) != layouts.Count)
            {
                throw Invalid($"talk-box layout {layouts.Count} missing");
            }

            Vec2I Pair(int column) => new(reader.GetInt32(column), reader.GetInt32(column + 1));
            layouts.Add(new TalkBoxLayout(
                reader.GetInt32(1),
                Pair(2),
                Pair(4),
                Pair(6),
                Pair(8),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetInt32(13),
                Pair(14)));
        }

        if (layouts.Count != LayoutCount)
        {
            throw Invalid($"talk-box layouts incomplete: {layouts.Count}");
        }

        return layouts;
    }

    /// <summary>Reads the list of decoded talk text files from <c>text_files</c>, then loads each <c>text/talkNN.json</c> and checks its entry count against the count recorded at extraction.</summary>
    /// <returns>Token lines keyed by text file index. Never empty.</returns>
    /// <exception cref="CacheResourceException">No text file is listed, a file is missing or malformed, or its entry count differs from the database.</exception>
    private Dictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>> ReadTexts()
    {
        var files = new List<(int Index, int Count)>();
        using (SqliteCommand command = Command("SELECT file_index, entry_count FROM text_files ORDER BY file_index"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                files.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }
        }

        if (files.Count == 0)
        {
            throw Invalid("dialogue pack has no text files");
        }

        var texts = new Dictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>>();
        foreach ((int index, int count) in files)
        {
            string path = DataPackFiles.Require(_pack, $"text/talk{index.ToString("D2", CultureInfo.InvariantCulture)}.json", $"text file={index}");
            List<IReadOnlyList<TextToken>> entries = ParseTextFile(path, index);
            if (entries.Count != count)
            {
                throw Invalid($"text file={index} has {entries.Count} entries, expected {count}");
            }

            texts.Add(index, entries);
        }

        return texts;
    }

    /// <summary>Parses one talk text JSON file whose <c>entries</c> array holds one token array per message.</summary>
    /// <param name="path">Absolute path of the JSON file.</param>
    /// <param name="index">Text file index, used in the error message.</param>
    /// <returns>The messages in file order, each as a token list.</returns>
    /// <exception cref="CacheResourceException">The file cannot be read, is not valid JSON, or a token is malformed.</exception>
    private static List<IReadOnlyList<TextToken>> ParseTextFile(string path, int index)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var entries = new List<IReadOnlyList<TextToken>>();
            foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
            {
                entries.Add([.. entry.EnumerateArray().Select(ParseTextToken)]);
            }

            return entries;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            throw Invalid($"text file={index} invalid: {error.Message}");
        }
    }

    /// <summary>Decodes one token of a talk text file: <c>g</c> glyph and <c>i</c> icon carry a code in <c>c</c>, <c>c</c> control tokens carry a command in <c>cmd</c> and integer arguments in <c>args</c>.</summary>
    /// <param name="token">A token object with a <c>t</c> type field.</param>
    /// <returns>The runtime token.</returns>
    /// <exception cref="JsonException">The type field is null or not one of <c>g</c>, <c>i</c>, <c>c</c>.</exception>
    private static TextToken ParseTextToken(JsonElement token)
    {
        string type = token.GetProperty("t").GetString() ?? throw new JsonException("token type missing");
        return type switch
        {
            "g" => new TextToken("g", token.GetProperty("c").GetInt32(), 0, []),
            "i" => new TextToken("i", token.GetProperty("c").GetInt32(), 0, []),
            "c" => new TextToken("c", 0, token.GetProperty("cmd").GetInt32(), [.. token.GetProperty("args").EnumerateArray().Select(arg => arg.GetInt32())]),
            _ => throw new JsonException($"unknown token type {type}"),
        };
    }

    /// <summary>Assembles the font: line heights and metrics of the three styles, the two glyph page PNGs of the field dialogue style (style 1) and the pack font directory for the other pages.</summary>
    /// <returns>The font, whose top-level glyphs, icons and line height are those of style 1.</returns>
    /// <exception cref="CacheResourceException">A style contract is broken, a page PNG is missing or the font directory is rejected.</exception>
    private FontData ReadFont()
    {
        Dictionary<int, int> lineHeights = ReadFontLineHeights();
        Dictionary<int, FontStyleData> styles = ReadFontStyles(lineHeights);
        FontStyleData dialogue = styles[DialogueStyle];
        string directory = ResolveFontDirectory();
        return new FontData(
            dialogue.Glyphs,
            dialogue.Icons,
            DataPackFiles.Require(_pack, "font/font-style-1-page-0.png", "dialogue font page 0"),
            DataPackFiles.Require(_pack, "font/font-style-1-page-1.png", "dialogue font page 1"),
            dialogue.LineHeight,
            styles,
            directory);
    }

    /// <summary>Reads the line height in pixels of each font style from <c>font_styles</c>.</summary>
    /// <returns>Line heights keyed by style id, as stored. Validation is left to the caller.</returns>
    private Dictionary<int, int> ReadFontLineHeights()
    {
        var lineHeights = new Dictionary<int, int>();
        using SqliteCommand command = Command("SELECT style_id, line_height FROM font_styles ORDER BY style_id");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            lineHeights.Add(reader.GetInt32(0), reader.GetInt32(1));
        }

        return lineHeights;
    }

    /// <summary>Loads glyph and icon metrics for styles 1, 2 and 3 and checks each style's line height against the expected 14, 11 and 9 pixels, so a pack with a shifted font table is rejected.</summary>
    /// <param name="lineHeights">Line heights read from <c>font_styles</c>, keyed by style id.</param>
    /// <returns>The three styles keyed by style id.</returns>
    /// <exception cref="CacheResourceException">A style is absent, has the wrong line height, or has no glyph or icon metrics.</exception>
    private Dictionary<int, FontStyleData> ReadFontStyles(Dictionary<int, int> lineHeights)
    {
        var styles = new Dictionary<int, FontStyleData>();
        foreach ((int style, int expectedLineHeight) in StyleLineHeights)
        {
            Dictionary<int, GlyphMetric> glyphs = ReadMetrics("font_glyphs", style);
            Dictionary<int, GlyphMetric> icons = ReadMetrics("font_icons", style);
            if (!lineHeights.TryGetValue(style, out int lineHeight) || lineHeight != expectedLineHeight || glyphs.Count == 0 || icons.Count == 0)
            {
                throw Invalid($"font style {style} contract invalid");
            }

            styles.Add(style, new FontStyleData(glyphs, icons, lineHeight));
        }

        return styles;
    }

    /// <summary>Resolves the pack's <c>font</c> directory, which holds one PNG per style and page.</summary>
    /// <returns>The absolute directory path. Its existence is not checked here.</returns>
    /// <exception cref="CacheResourceException">The pack rejects the path.</exception>
    private string ResolveFontDirectory()
    {
        try
        {
            return _pack.ResolvePath("font");
        }
        catch (ArgumentException error)
        {
            throw Invalid($"font directory invalid: {error.Message}");
        }
    }

    /// <summary>Reads the metrics of one style from a metrics table: glyph page, texel origin (u, v), CLUT position, size, drawing offset and advance, all in pixels.</summary>
    /// <param name="table">Either <c>font_glyphs</c> or <c>font_icons</c>. The value is a fixed literal, never user input.</param>
    /// <param name="style">Font style id, 1 to 3.</param>
    /// <returns>Metrics keyed by character or icon code. Empty when the style has no row.</returns>
    private Dictionary<int, GlyphMetric> ReadMetrics(string table, int style)
    {
        var metrics = new Dictionary<int, GlyphMetric>();
        using SqliteCommand command = Command($"SELECT code, page, u, v, clut_x, clut_y, width, height, dx, dy, advance FROM {table} WHERE style_id=$style ORDER BY code");
        command.Parameters.AddWithValue("$style", style);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            metrics.Add(reader.GetInt32(0), new GlyphMetric(
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10)));
        }

        return metrics;
    }

    /// <summary>Runs a load step and wraps SQLite and data conversion failures into a cache error, so the host reports one error type for any malformed pack.</summary>
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

    /// <summary>Creates a command on the loader's connection with the given SQL text.</summary>
    /// <param name="sql">SQL statement. Only constant statements of this class are passed.</param>
    /// <returns>A new command the caller must dispose.</returns>
    private SqliteCommand Command(string sql)
    {
        SqliteCommand command = _database.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    /// <summary>Builds the error used when a dialogue record exists but breaks the pack contract.</summary>
    /// <param name="detail">Description of the defect.</param>
    /// <returns>An exception with code <c>CACHE_INVALID</c>, not yet thrown.</returns>
    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);
}
