using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>Static helpers that build the per-zone dialogue tree from SQLite rows. The queries
/// are issued in one pass each; the results are assembled in memory and never lazily evaluated.</summary>
internal static class DialogueZoneReader
{
    /// <summary>Reads all dialogue zones from the open database connection and returns them keyed by zone id.</summary>
    /// <param name="database">The open SQLite connection to the data pack database.</param>
    /// <returns>A dictionary of zone dialogue data keyed by zone id string (e.g. "S200").</returns>
    /// <exception cref="CacheResourceException">A dialogue code list name is unrecognised.</exception>
    internal static Dictionary<string, ZoneDialogue> ReadZones(SqliteConnection database)
    {
        var conditions = new Dictionary<(int Stage, int Actor, int Talk), List<DialogueCondition>>();
        var actions = new Dictionary<(int Stage, int Actor, int Talk), List<DialogueAction>>();
        ReadCodeRows(database, conditions, actions);

        var families = ReadFamilyRows(database);
        var talks = ReadTalkRows(database, conditions, actions, families);
        var actors = ReadActorRows(database, talks);
        return ReadZoneRows(database, actors);
    }

    private static void ReadCodeRows(
        SqliteConnection database,
        Dictionary<(int Stage, int Actor, int Talk), List<DialogueCondition>> conditions,
        Dictionary<(int Stage, int Actor, int Talk), List<DialogueAction>> actions)
    {
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT stage_id, actor_index, talk_index, list_kind, code, value, kind FROM dialogue_codes ORDER BY stage_id, actor_index, talk_index, list_kind, ordinal";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = (Stage: reader.GetInt32(0), Actor: reader.GetInt32(1), Talk: reader.GetInt32(2));
            string list = reader.GetString(3);
            int code = reader.GetInt32(4), value = reader.GetInt32(5);
            string kind = reader.GetString(6);
            AppendCodeRow(list, key, code, value, kind, conditions, actions);
        }
    }

    private static void AppendCodeRow(
        string list,
        (int Stage, int Actor, int Talk) key,
        int code,
        int value,
        string kind,
        Dictionary<(int Stage, int Actor, int Talk), List<DialogueCondition>> conditions,
        Dictionary<(int Stage, int Actor, int Talk), List<DialogueAction>> actions)
    {
        if (list == "condition")
        {
            Append(conditions, key, new DialogueCondition(code, value, kind));
        }
        else if (list == "action")
        {
            Append(actions, key, new DialogueAction(code, value, kind));
        }
        else
        {
            throw new CacheResourceException("CACHE_INVALID", $"dialogue code list {list} is unknown");
        }
    }

    private static Dictionary<(int Stage, int Actor, int Talk), List<string>> ReadFamilyRows(SqliteConnection database)
    {
        var families = new Dictionary<(int Stage, int Actor, int Talk), List<string>>();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT stage_id, actor_index, talk_index, family FROM dialogue_talk_families ORDER BY stage_id, actor_index, talk_index, family";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            Append(families, (Stage: reader.GetInt32(0), Actor: reader.GetInt32(1), Talk: reader.GetInt32(2)), reader.GetString(3));
        }

        return families;
    }

    private static Dictionary<(int Stage, int Actor), List<DialogueTalk>> ReadTalkRows(
        SqliteConnection database,
        Dictionary<(int Stage, int Actor, int Talk), List<DialogueCondition>> conditions,
        Dictionary<(int Stage, int Actor, int Talk), List<DialogueAction>> actions,
        Dictionary<(int Stage, int Actor, int Talk), List<string>> families)
    {
        var talks = new Dictionary<(int Stage, int Actor), List<DialogueTalk>>();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT stage_id, actor_index, talk_index, text_entry, selected, kind FROM dialogue_talks ORDER BY stage_id, actor_index, talk_index";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = (Stage: reader.GetInt32(0), Actor: reader.GetInt32(1), Talk: reader.GetInt32(2));
            conditions.TryGetValue(key, out List<DialogueCondition>? cList);
            actions.TryGetValue(key, out List<DialogueAction>? aList);
            families.TryGetValue(key, out List<string>? fList);
            Append(talks, (Stage: key.Stage, Actor: key.Actor), new DialogueTalk(
                reader.GetInt32(3), cList ?? [], aList ?? [], reader.GetString(5), fList ?? [], reader.GetInt32(4) != 0));
        }

        return talks;
    }

    private static Dictionary<int, List<DialogueActor>> ReadActorRows(
        SqliteConnection database,
        Dictionary<(int Stage, int Actor), List<DialogueTalk>> talks)
    {
        var actors = new Dictionary<int, List<DialogueActor>>();
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT stage_id, actor_index, x, y, direction, width FROM dialogue_actors ORDER BY stage_id, actor_index";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int stage = reader.GetInt32(0), index = reader.GetInt32(1);
            talks.TryGetValue((Stage: stage, Actor: index), out List<DialogueTalk>? tList);
            Append(actors, stage, new DialogueActor(index, reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), tList ?? []));
        }

        return actors;
    }

    private static Dictionary<string, ZoneDialogue> ReadZoneRows(
        SqliteConnection database,
        Dictionary<int, List<DialogueActor>> actors)
    {
        var zones = new Dictionary<string, ZoneDialogue>(StringComparer.Ordinal);
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT stage_id, talk_file FROM dialogue_zones ORDER BY stage_id";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            int stage = reader.GetInt32(0);
            actors.TryGetValue(stage, out List<DialogueActor>? aList);
            zones.Add($"S{stage.ToString("D3", CultureInfo.InvariantCulture)}", new ZoneDialogue(reader.GetInt32(1), aList ?? []));
        }

        return zones;
    }

    private static void Append<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out List<TValue>? list))
        {
            list = [];
            map.Add(key, list);
        }

        list.Add(value);
    }
}
