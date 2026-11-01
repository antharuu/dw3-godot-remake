using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Writes numeric FieldTalk data recovered from stage overlays into the data pack.</summary>
public static class DialogueDomainWriter
{
    private const int SnapshotProgress = 4;
    private static readonly HashSet<ushort> NonDialogueModes = [0x204, 0x22D];

    public static bool IsDialogueZone(StageDialogueAnalysis stage) => !NonDialogueModes.Contains(stage.Mode);

    public static void ValidateDialogueStages(IReadOnlyList<StageDialogueAnalysis> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        StageDialogueAnalysis? invalid = stages.FirstOrDefault(stage => !IsDialogueZone(stage));
        if (invalid is not null)
            throw new ArgumentException($"Stage mode 0x{invalid.Mode:X3} (WSTAG{invalid.StageId:D3}) is not a dialogue zone.", nameof(stages));
        if (stages.Select(stage => stage.StageId).Distinct().Count() != stages.Count)
            throw new ArgumentException("Dialogue stages contain a duplicate stage id.", nameof(stages));
    }

    public static void Write(
        DataPackWriteContext context,
        IReadOnlyList<StageDialogueAnalysis> stages,
        ProgressConditionTables progress,
        int snapshotProgress = SnapshotProgress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(progress);
        ValidateDialogueStages(stages);

        foreach (StageDialogueAnalysis stage in stages.OrderBy(stage => stage.StageId))
        {
            Insert(context, "INSERT INTO dialogue_zones (stage_id,mode,talk_file) VALUES ($1,$2,$3)",
                stage.StageId, stage.Mode, stage.TextFile.TalkIndex);
            foreach (StageActorDialogue actor in stage.Actors.OrderBy(actor => actor.Index))
            {
                Insert(context, "INSERT INTO dialogue_actors (stage_id,actor_index,x,y,direction,width) VALUES ($1,$2,$3,$4,$5,$6)",
                    stage.StageId, actor.Index, actor.Entry.X, actor.Entry.Y, actor.Entry.Direction, actor.Width);
                int? selected = SelectTalk(actor.Talks, progress, snapshotProgress);
                for (int talkIndex = 0; talkIndex < actor.Talks.Count; talkIndex++)
                {
                    FieldTalk talk = actor.Talks[talkIndex];
                    string talkKind = talk.Actions.Count == 0 ? "simple" : "effect";
                    Insert(context, "INSERT INTO dialogue_talks (stage_id,actor_index,talk_index,text_entry,selected,kind) VALUES ($1,$2,$3,$4,$5,$6)",
                        stage.StageId, actor.Index, talkIndex, talk.Entry, selected == talkIndex ? 1 : 0, talkKind);
                    WriteCodes(context, stage.StageId, actor.Index, talkIndex, "condition", talk.Conditions, ConditionKind);
                    WriteCodes(context, stage.StageId, actor.Index, talkIndex, "action", talk.Actions, ActionKind);
                    foreach (string family in talk.Actions.Select(item => ActionKind(item.Code)).Distinct().Order(StringComparer.Ordinal))
                        Insert(context, "INSERT INTO dialogue_talk_families (stage_id,actor_index,talk_index,family) VALUES ($1,$2,$3,$4)",
                            stage.StageId, actor.Index, talkIndex, family);
                }
            }
        }
        WriteManifest(context, stages, progress, snapshotProgress);
    }

    private static int? SelectTalk(IReadOnlyList<FieldTalk> talks, ProgressConditionTables progress, int snapshotProgress)
    {
        int? selected = null;
        for (int index = 0; index < talks.Count; index++)
        {
            FieldTalk talk = talks[index];
            if (talk.Conditions.Count == 0)
            {
                selected ??= index;
                break;
            }
            if (NpcSnapshotEvaluator.IsVisible(talk.Conditions, snapshotProgress, progress.Ranges, progress.SpecialProgressRanges))
            {
                selected = index;
                break;
            }
        }
        return selected;
    }

    private static void WriteManifest(
        DataPackWriteContext context,
        IReadOnlyList<StageDialogueAnalysis> stages,
        ProgressConditionTables progress,
        int snapshotProgress)
    {
        string path = context.GetImagePath("dialogue/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartObject();
        json.WriteString("schema", "m4-dialogue-v1");
        json.WriteString("edition", "SLES-03936");
        json.WriteStartObject("zones");
        foreach (StageDialogueAnalysis stage in stages.OrderBy(stage => stage.StageId))
        {
            json.WriteStartObject($"S{stage.StageId:D3}");
            json.WriteNumber("talk_file", stage.TextFile.TalkIndex);
            json.WriteStartArray("actors");
            foreach (StageActorDialogue actor in stage.Actors.OrderBy(actor => actor.Index))
            {
                int? selected = SelectTalk(actor.Talks, progress, snapshotProgress);
                json.WriteStartObject();
                json.WriteNumber("id", actor.Index);
                json.WriteNumber("x", actor.Entry.X);
                json.WriteNumber("y", actor.Entry.Y);
                json.WriteNumber("dir", actor.Entry.Direction);
                json.WriteNumber("width", actor.Width);
                json.WriteStartArray("talks");
                for (int talkIndex = 0; talkIndex < actor.Talks.Count; talkIndex++)
                {
                    FieldTalk talk = actor.Talks[talkIndex];
                    json.WriteStartObject();
                    json.WriteNumber("entry", talk.Entry);
                    WriteCodes(json, "conditions", talk.Conditions, ConditionKind);
                    WriteCodes(json, "actions", talk.Actions, ActionKind);
                    json.WriteString("kind", talk.Actions.Count == 0 ? "simple" : "effect");
                    json.WriteStartArray("families");
                    foreach (string family in talk.Actions.Select(item => ActionKind(item.Code)).Distinct().Order(StringComparer.Ordinal))
                        json.WriteStringValue(family);
                    json.WriteEndArray();
                    json.WriteBoolean("selected", selected == talkIndex);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        json.WriteEndObject();
        json.WriteEndObject();
        json.Flush();
    }

    private static void WriteCodes(Utf8JsonWriter json, string name, IReadOnlyList<NpcCondition> codes, Func<int, string> kind)
    {
        json.WriteStartArray(name);
        foreach (NpcCondition item in codes)
        {
            json.WriteStartObject();
            json.WriteNumber("code", item.Code);
            json.WriteNumber("value", item.Value);
            json.WriteString("kind", kind(item.Code));
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    private static void WriteCodes(
        DataPackWriteContext context, int stageId, int actorIndex, int talkIndex,
        string listKind, IReadOnlyList<NpcCondition> codes, Func<int, string> kind)
    {
        for (int ordinal = 0; ordinal < codes.Count; ordinal++)
        {
            NpcCondition item = codes[ordinal];
            Insert(context, "INSERT INTO dialogue_codes (stage_id,actor_index,talk_index,list_kind,ordinal,code,value,kind) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)",
                stageId, actorIndex, talkIndex, listKind, ordinal, item.Code, item.Value, kind(item.Code));
        }
    }

    private static string ConditionKind(int code)
    {
        int group = (code >> 8) & 0xFE;
        if (group is >= 0x00 and <= 0x40) return "flag";
        return group switch
        {
            0x60 => "progress",
            0x70 => "special",
            >= 0x80 and <= 0x8E => "item",
            0x90 => "start_event",
            _ => "none",
        };
    }

    private static string ActionKind(int code)
    {
        int group = (code >> 8) & 0xFE;
        if (group is >= 0x00 and <= 0x40) return "flag_set";
        return group switch
        {
            0x70 => "special",
            0x74 => "event_battle",
            0x76 or 0x78 => "card_battle",
            0x7A or 0x7C or 0x94 => "mode_change",
            >= 0x80 and <= 0x8E => "item",
            0x90 => "start_event",
            0x92 => "card",
            _ => "unknown",
        };
    }

    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        command.ExecuteNonQuery();
    }
}
