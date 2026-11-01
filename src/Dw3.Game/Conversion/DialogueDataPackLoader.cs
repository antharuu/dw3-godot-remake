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

    public DialogueDataPackLoader(DataPackReader pack)
    {
        _pack = pack ?? throw new ArgumentNullException(nameof(pack));
        _database = pack.OpenDatabase();
    }

    public DialogueData LoadDialogue() => Read("dialogue", () =>
    {
        IReadOnlyDictionary<string, ZoneDialogue> zones = ReadZones();
        if (zones.Count == 0) throw Invalid("dialogue pack has no zones");
        IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>> texts = ReadTexts();
        foreach ((string zone, ZoneDialogue dialogue) in zones)
            if (!texts.ContainsKey(dialogue.TalkFile))
                throw Invalid($"dialogue zone={zone} uses absent text file {dialogue.TalkFile}");
        return new DialogueData(zones, texts, ReadFont());
    });

    public DialogueFrameData LoadDialogueFrame() => Read("talk box", () =>
    {
        var frames = new Dictionary<int, DialogueFrame>();
        using (SqliteCommand command = Command("SELECT frame_id, width, height, clut_frames, semi, image_path FROM talk_box_frames ORDER BY frame_id"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                frames.Add(id, new DialogueFrame(id, DataPackFiles.Require(_pack, reader.GetString(5), $"talk-box frame={id}"),
                    reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4) != 0));
            }
        foreach (int required in RequiredFrames)
        {
            if (!frames.ContainsKey(required)) throw Invalid($"talk-box frame {required} missing");
        }

        PanelSpec? panel = null;
        using (SqliteCommand command = Command("SELECT red, green, blue, semi, texture_page, clut, image_path, width, height FROM talk_box_panel"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            if (reader.Read())
                panel = new PanelSpec(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3) != 0,
                    reader.GetInt32(4), reader.GetInt32(5), DataPackFiles.Require(_pack, reader.GetString(6), "talk-box panel"),
                    reader.GetInt32(7), reader.GetInt32(8));
            if (panel is null || reader.Read()) throw Invalid("talk-box panel must have exactly one record");
        }

        var layouts = new List<TalkBoxLayout>();
        using (SqliteCommand command = Command("""
            SELECT layout, sprite, sprite_x, sprite_y, sprite0_x, sprite0_y, panel_x, panel_y, sprite2_x, sprite2_y,
                   name_x, name_y, text_x, text_y, arrow_x, arrow_y
            FROM talk_box_layouts ORDER BY layout
            """))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                if (reader.GetInt32(0) != layouts.Count) throw Invalid($"talk-box layout {layouts.Count} missing");
                Vec2I Pair(int column) => new(reader.GetInt32(column), reader.GetInt32(column + 1));
                layouts.Add(new TalkBoxLayout(reader.GetInt32(1), Pair(2), Pair(4), Pair(6), Pair(8),
                    reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), Pair(14)));
            }
        if (layouts.Count != LayoutCount) throw Invalid($"talk-box layouts incomplete: {layouts.Count}");
        return new DialogueFrameData(frames, layouts, panel);
    });

    public void Dispose() => _database.Dispose();

    private IReadOnlyDictionary<string, ZoneDialogue> ReadZones()
    {
        var conditions = new Dictionary<(int, int, int), List<DialogueCondition>>();
        var actions = new Dictionary<(int, int, int), List<DialogueAction>>();
        using (SqliteCommand command = Command("SELECT stage_id, actor_index, talk_index, list_kind, code, value, kind FROM dialogue_codes ORDER BY stage_id, actor_index, talk_index, list_kind, ordinal"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                string list = reader.GetString(3);
                int code = reader.GetInt32(4), value = reader.GetInt32(5);
                string kind = reader.GetString(6);
                if (list == "condition") Append(conditions, key, new DialogueCondition(code, value, kind));
                else if (list == "action") Append(actions, key, new DialogueAction(code, value, kind));
                else throw Invalid($"dialogue code list {list} is unknown");
            }

        var families = new Dictionary<(int, int, int), List<string>>();
        using (SqliteCommand command = Command("SELECT stage_id, actor_index, talk_index, family FROM dialogue_talk_families ORDER BY stage_id, actor_index, talk_index, family"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
                Append(families, (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)), reader.GetString(3));

        var talks = new Dictionary<(int, int), List<DialogueTalk>>();
        using (SqliteCommand command = Command("SELECT stage_id, actor_index, talk_index, text_entry, selected, kind FROM dialogue_talks ORDER BY stage_id, actor_index, talk_index"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                var key = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
                Append(talks, (key.Item1, key.Item2), new DialogueTalk(reader.GetInt32(3),
                    conditions.TryGetValue(key, out List<DialogueCondition>? c) ? c : [],
                    actions.TryGetValue(key, out List<DialogueAction>? a) ? a : [],
                    reader.GetString(5),
                    families.TryGetValue(key, out List<string>? f) ? f : [],
                    reader.GetInt32(4) != 0));
            }

        var actors = new Dictionary<int, List<DialogueActor>>();
        using (SqliteCommand command = Command("SELECT stage_id, actor_index, x, y, direction, width FROM dialogue_actors ORDER BY stage_id, actor_index"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int stage = reader.GetInt32(0), index = reader.GetInt32(1);
                Append(actors, stage, new DialogueActor(index, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                    talks.TryGetValue((stage, index), out List<DialogueTalk>? list) ? list : []));
            }

        var zones = new Dictionary<string, ZoneDialogue>(StringComparer.Ordinal);
        using (SqliteCommand command = Command("SELECT stage_id, talk_file FROM dialogue_zones ORDER BY stage_id"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read())
            {
                int stage = reader.GetInt32(0);
                zones.Add($"S{stage.ToString("D3", CultureInfo.InvariantCulture)}",
                    new ZoneDialogue(reader.GetInt32(1), actors.TryGetValue(stage, out List<DialogueActor>? list) ? list : []));
            }
        return zones;
    }

    private IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>> ReadTexts()
    {
        var files = new List<(int Index, int Count)>();
        using (SqliteCommand command = Command("SELECT file_index, entry_count FROM text_files ORDER BY file_index"))
        using (SqliteDataReader reader = command.ExecuteReader())
            while (reader.Read()) files.Add((reader.GetInt32(0), reader.GetInt32(1)));
        if (files.Count == 0) throw Invalid("dialogue pack has no text files");

        var texts = new Dictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>>();
        foreach ((int index, int count) in files)
        {
            string path = DataPackFiles.Require(_pack, $"text/talk{index.ToString("D2", CultureInfo.InvariantCulture)}.json", $"text file={index}");
            IReadOnlyList<IReadOnlyList<TextToken>> entries = ParseTextFile(path, index);
            if (entries.Count != count) throw Invalid($"text file={index} has {entries.Count} entries, expected {count}");
            texts.Add(index, entries);
        }
        return texts;
    }

    private static IReadOnlyList<IReadOnlyList<TextToken>> ParseTextFile(string path, int index)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            var entries = new List<IReadOnlyList<TextToken>>();
            foreach (JsonElement entry in document.RootElement.GetProperty("entries").EnumerateArray())
            {
                var tokens = new List<TextToken>();
                foreach (JsonElement token in entry.EnumerateArray())
                {
                    string type = token.GetProperty("t").GetString() ?? throw new JsonException("token type missing");
                    tokens.Add(type switch
                    {
                        "g" => new TextToken("g", token.GetProperty("c").GetInt32(), 0, []),
                        "i" => new TextToken("i", token.GetProperty("c").GetInt32(), 0, []),
                        "c" => new TextToken("c", 0, token.GetProperty("cmd").GetInt32(), [.. token.GetProperty("args").EnumerateArray().Select(arg => arg.GetInt32())]),
                        _ => throw new JsonException($"unknown token type {type}"),
                    });
                }
                entries.Add(tokens);
            }
            return entries;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            throw Invalid($"text file={index} invalid: {error.Message}");
        }
    }

    private FontData ReadFont()
    {
        var lineHeights = new Dictionary<int, int>();
        using (SqliteCommand command = Command("SELECT style_id, line_height FROM font_styles ORDER BY style_id"))
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read()) lineHeights.Add(reader.GetInt32(0), reader.GetInt32(1));
        }

        var styles = new Dictionary<int, FontStyleData>();
        foreach ((int style, int expectedLineHeight) in StyleLineHeights)
        {
            IReadOnlyDictionary<int, GlyphMetric> glyphs = ReadMetrics("font_glyphs", style);
            IReadOnlyDictionary<int, GlyphMetric> icons = ReadMetrics("font_icons", style);
            if (!lineHeights.TryGetValue(style, out int lineHeight) || lineHeight != expectedLineHeight || glyphs.Count == 0 || icons.Count == 0)
                throw Invalid($"font style {style} contract invalid");
            styles.Add(style, new FontStyleData(glyphs, icons, lineHeight));
        }
        FontStyleData dialogue = styles[DialogueStyle];
        string directory;
        try
        {
            directory = _pack.ResolvePath("font");
        }
        catch (ArgumentException error)
        {
            throw Invalid($"font directory invalid: {error.Message}");
        }
        return new FontData(dialogue.Glyphs, dialogue.Icons,
            DataPackFiles.Require(_pack, "font/font-style-1-page-0.png", "dialogue font page 0"),
            DataPackFiles.Require(_pack, "font/font-style-1-page-1.png", "dialogue font page 1"),
            dialogue.LineHeight, styles, directory);
    }

    private IReadOnlyDictionary<int, GlyphMetric> ReadMetrics(string table, int style)
    {
        var metrics = new Dictionary<int, GlyphMetric>();
        using SqliteCommand command = Command($"SELECT code, page, u, v, clut_x, clut_y, width, height, dx, dy, advance FROM {table} WHERE style_id=$style ORDER BY code");
        command.Parameters.AddWithValue("$style", style);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            metrics.Add(reader.GetInt32(0), new GlyphMetric(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10)));
        return metrics;
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

    private SqliteCommand Command(string sql)
    {
        SqliteCommand command = _database.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private static void Append<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value) where TKey : notnull
    {
        if (!map.TryGetValue(key, out List<TValue>? list)) map.Add(key, list = []);
        list.Add(value);
    }

    private static CacheResourceException Invalid(string detail) => new("CACHE_INVALID", detail);
}
