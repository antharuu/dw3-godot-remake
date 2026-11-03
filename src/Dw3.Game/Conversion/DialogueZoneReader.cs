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

    /// <summary>Reads <c>dialogue_codes</c>, where each row is one condition or action code of a talk entry, and sorts the rows into the two maps. Row order (by <c>ordinal</c>) is kept so codes are evaluated in their disc order.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="conditions">Receives the condition codes, keyed by stage, actor index and talk index.</param>
    /// <param name="actions">Receives the action codes, with the same key.</param>
    /// <exception cref="CacheResourceException">A row has a <c>list_kind</c> other than <c>condition</c> or <c>action</c>.</exception>
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

    /// <summary>Routes one code row to the condition or action map according to its list kind.</summary>
    /// <param name="list">Value of <c>list_kind</c>: <c>condition</c> or <c>action</c>.</param>
    /// <param name="key">Stage id, actor index and talk index the code belongs to.</param>
    /// <param name="code">Raw condition or action opcode.</param>
    /// <param name="value">Operand of the opcode, for example a flag number.</param>
    /// <param name="kind">Semantic label written by the extractor for the opcode.</param>
    /// <param name="conditions">Map that receives condition codes.</param>
    /// <param name="actions">Map that receives action codes.</param>
    /// <exception cref="CacheResourceException"><paramref name="list"/> is neither <c>condition</c> nor <c>action</c>.</exception>
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

    /// <summary>Reads <c>dialogue_talk_families</c>, the labels that group talk entries sharing the same story context (several rows per talk are allowed).</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <returns>Family labels keyed by stage, actor index and talk index, sorted alphabetically per key.</returns>
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

    /// <summary>Reads <c>dialogue_talks</c> and attaches to each talk its conditions, actions and families. A talk without codes or families gets empty lists rather than null.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="conditions">Condition codes keyed by stage, actor and talk.</param>
    /// <param name="actions">Action codes keyed by stage, actor and talk.</param>
    /// <param name="families">Family labels keyed by stage, actor and talk.</param>
    /// <returns>Talks keyed by stage and actor, in talk index order, which is the order the runtime tests them.</returns>
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

    /// <summary>Reads <c>dialogue_actors</c>: the tile position, facing direction and interaction width of every speaking actor, with the talks found for it.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="talks">Talks keyed by stage and actor index.</param>
    /// <returns>Actors keyed by stage id, in actor index order.</returns>
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

    /// <summary>Reads <c>dialogue_zones</c>, which ties each stage to the talk text file it uses, and names each zone <c>Snnn</c> with the stage id on three digits.</summary>
    /// <param name="database">Open pack database connection.</param>
    /// <param name="actors">Actors keyed by stage id.</param>
    /// <returns>Zones keyed by name. A stage without actors gets an empty actor list.</returns>
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

    /// <summary>Adds a value to the list stored under a key, creating the list on first use.</summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <typeparam name="TValue">Element type of the lists.</typeparam>
    /// <param name="map">Map of lists to update.</param>
    /// <param name="key">Key of the list.</param>
    /// <param name="value">Value appended at the end, so insertion order is kept.</param>
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
