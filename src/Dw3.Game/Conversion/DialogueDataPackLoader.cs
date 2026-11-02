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

    private Dictionary<string, ZoneDialogue> ReadZones() => DialogueZoneReader.ReadZones(_database);

    private Dictionary<int, DialogueFrame> ReadTalkBoxFrames()
    {
        var frames = new Dictionary<int, DialogueFrame>();
        using SqliteCommand command = Command("SELECT frame_id, width, height, clut_frames, semi, image_path FROM talk_box_frames ORDER BY frame_id");
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int id = reader.GetInt32(0);
            frames.Add(id, new DialogueFrame(id, DataPackFiles.Require(_pack, reader.GetString(5), $"talk-box frame={id}"),
                reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4) != 0));
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

    private PanelSpec ReadTalkBoxPanel()
    {
        PanelSpec? panel = null;
        using SqliteCommand command = Command("SELECT red, green, blue, semi, texture_page, clut, image_path, width, height FROM talk_box_panel");
        using SqliteDataReader reader = command.ExecuteReader();
        if (reader.Read())
        {
            panel = new PanelSpec(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3) != 0,
                reader.GetInt32(4), reader.GetInt32(5), DataPackFiles.Require(_pack, reader.GetString(6), "talk-box panel"),
                reader.GetInt32(7), reader.GetInt32(8));
        }

        if (panel is null || reader.Read())
        {
            throw Invalid("talk-box panel must have exactly one record");
        }

        return panel;
    }

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
            layouts.Add(new TalkBoxLayout(reader.GetInt32(1), Pair(2), Pair(4), Pair(6), Pair(8),
                reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), Pair(14)));
        }

        if (layouts.Count != LayoutCount)
        {
            throw Invalid($"talk-box layouts incomplete: {layouts.Count}");
        }

        return layouts;
    }

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

    private FontData ReadFont()
    {
        Dictionary<int, int> lineHeights = ReadFontLineHeights();
        Dictionary<int, FontStyleData> styles = ReadFontStyles(lineHeights);
        FontStyleData dialogue = styles[DialogueStyle];
        string directory = ResolveFontDirectory();
        return new FontData(dialogue.Glyphs, dialogue.Icons,
            DataPackFiles.Require(_pack, "font/font-style-1-page-0.png", "dialogue font page 0"),
            DataPackFiles.Require(_pack, "font/font-style-1-page-1.png", "dialogue font page 1"),
            dialogue.LineHeight, styles, directory);
    }

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

    private Dictionary<int, GlyphMetric> ReadMetrics(string table, int style)
    {
        var metrics = new Dictionary<int, GlyphMetric>();
        using SqliteCommand command = Command($"SELECT code, page, u, v, clut_x, clut_y, width, height, dx, dy, advance FROM {table} WHERE style_id=$style ORDER BY code");
        command.Parameters.AddWithValue("$style", style);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            metrics.Add(reader.GetInt32(0), new GlyphMetric(
                reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8),
                reader.GetInt32(9), reader.GetInt32(10)));
        }

        return metrics;
    }

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

    private SqliteCommand Command(string sql)
    {
        SqliteCommand command = _database.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);
}
