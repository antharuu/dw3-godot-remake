using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Projects recovered dialogue records into the stable JSON manifest representation.
/// </summary>
internal static class DialogueManifestWriter
{
    /// <summary>
    /// Evaluates the condition tables and snapshot progress to select the first applicable talk index.
    /// </summary>
    /// <param name="talks">The ordered talk alternatives for the actor.</param>
    /// <param name="progress">The progress condition tables with range and special-progress definitions.</param>
    /// <param name="snapshotProgress">The current snapshot progress value used for condition evaluation.</param>
    /// <returns>The zero-based index of the first applicable talk, or null when no conditions match.</returns>
    internal static int? SelectTalk(IReadOnlyList<FieldTalk> talks, ProgressConditionTables progress, int snapshotProgress)
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

    /// <summary>Writes the full dialogue manifest JSON to the data pack for all supplied stages.</summary>
    /// <param name="context">The data pack write context providing image path resolution.</param>
    /// <param name="stages">The analysed stage dialogue records ordered by stage id.</param>
    /// <param name="progress">The progress condition tables for conditional talk selection.</param>
    /// <param name="snapshotProgress">The snapshot progress value used for condition evaluation.</param>
    internal static void WriteManifest(
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
                    WriteTalk(json, actor.Talks[talkIndex], selected == talkIndex);
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

    internal static void WriteCodes(Utf8JsonWriter json, string name, IReadOnlyList<NpcCondition> codes, Func<int, string> kind)
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

    internal static string ConditionKind(int code)
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

    internal static string ActionKind(int code)
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

    /// <summary>Writes a single talk entry object with entry, conditions, actions, kind, families and selected flag.</summary>
    /// <param name="json">The JSON writer to append to.</param>
    /// <param name="talk">The talk to serialize.</param>
    /// <param name="selected">Whether this talk was selected by the snapshot evaluator.</param>
    private static void WriteTalk(Utf8JsonWriter json, FieldTalk talk, bool selected)
    {
        json.WriteStartObject();
        json.WriteNumber("entry", talk.Entry);
        WriteCodes(json, "conditions", talk.Conditions, ConditionKind);
        WriteCodes(json, "actions", talk.Actions, ActionKind);
        json.WriteString("kind", talk.Actions.Count == 0 ? "simple" : "effect");
        json.WriteStartArray("families");
        foreach (string family in talk.Actions.Select(item => ActionKind(item.Code)).Distinct().Order(StringComparer.Ordinal))
            json.WriteStringValue(family);
        json.WriteEndArray();
        json.WriteBoolean("selected", selected);
        json.WriteEndObject();
    }
}
