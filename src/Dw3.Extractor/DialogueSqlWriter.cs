using Microsoft.Data.Sqlite;
using Dw3.Data;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Persists dialogue projection rows into the pack database without writing manifest files.
/// </summary>
internal static class DialogueSqlWriter
{
    internal static void Write(
        DataPackWriteContext context,
        IReadOnlyList<StageDialogueAnalysis> stages,
        ProgressConditionTables progress,
        int snapshotProgress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(progress);

        foreach (StageDialogueAnalysis stage in stages.OrderBy(stage => stage.StageId))
        {
            Insert(context, "INSERT INTO dialogue_zones (stage_id,mode,talk_file) VALUES ($1,$2,$3)",
                stage.StageId, stage.Mode, stage.TextFile.TalkIndex);
            foreach (StageActorDialogue actor in stage.Actors.OrderBy(actor => actor.Index))
            {
                Insert(context, "INSERT INTO dialogue_actors (stage_id,actor_index,x,y,direction,width) VALUES ($1,$2,$3,$4,$5,$6)",
                    stage.StageId, actor.Index, actor.Entry.X, actor.Entry.Y, actor.Entry.Direction, actor.Width);
                int? selected = DialogueManifestWriter.SelectTalk(actor.Talks, progress, snapshotProgress);
                for (int talkIndex = 0; talkIndex < actor.Talks.Count; talkIndex++)
                {
                    FieldTalk talk = actor.Talks[talkIndex];
                    string talkKind = talk.Actions.Count == 0 ? "simple" : "effect";
                    Insert(context, "INSERT INTO dialogue_talks (stage_id,actor_index,talk_index,text_entry,selected,kind) VALUES ($1,$2,$3,$4,$5,$6)",
                        stage.StageId, actor.Index, talkIndex, talk.Entry, selected == talkIndex ? 1 : 0, talkKind);
                    InsertCodes(context, stage.StageId, actor.Index, talkIndex, "condition", talk.Conditions, DialogueManifestWriter.ConditionKind);
                    InsertCodes(context, stage.StageId, actor.Index, talkIndex, "action", talk.Actions, DialogueManifestWriter.ActionKind);
                    foreach (string family in talk.Actions.Select(item => DialogueManifestWriter.ActionKind(item.Code)).Distinct().Order(StringComparer.Ordinal))
                        Insert(context, "INSERT INTO dialogue_talk_families (stage_id,actor_index,talk_index,family) VALUES ($1,$2,$3,$4)",
                            stage.StageId, actor.Index, talkIndex, family);
                }
            }
        }

    }
    internal static void InsertCodes(
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
    internal static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        command.ExecuteNonQuery();
    }
}
