using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Writes bounded fighter action scripts into the battle data-pack contract.</summary>
internal static class BattleActionDomainWriter
{
    private const string Schema = "m9b-battle-actions-v3";

    /// <summary>Persists every resolved action archive with an explicit decoded or unsupported status.</summary>
    /// <param name="context">Current data-pack write context.</param>
    /// <param name="actions">Action archives resolved from fighter records.</param>
    internal static void Write(DataPackWriteContext context, IReadOnlyList<BattleActionExtraction> actions)
    {
        foreach (BattleActionExtraction action in actions)
        {
            string? path = action.Script is null ? null : $"battle/actions/fighter-{action.FighterId}-script-{action.ScriptSelector}.json";
            Insert(context, "INSERT INTO battle_action_archives (fighter_id,script_selector,effect_file,effect_archive_entry,status,reason,manifest_path) VALUES ($1,$2,$3,$4,$5,$6,$7)", action.FighterId, action.ScriptSelector, action.EffectFile, action.EffectArchiveEntry, action.Script is null ? "unsupported" : "decoded", (object?)action.Reason ?? DBNull.Value, (object?)path ?? DBNull.Value);
            if (action.Script is null)
            {
                continue;
            }

            string absolute = context.GetImagePath(path!);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, JsonSerializer.Serialize(new
            {
                schema = Schema,
                fighterId = action.FighterId,
                effectFile = action.EffectFile,
                effectArchiveEntry = action.EffectArchiveEntry,
                scriptSelector = action.ScriptSelector,
                commands = action.Script.Commands.Select(command => new { opcode = (int)command.Opcode, arguments = command.Arguments }),
            }));
            Insert(context, "INSERT INTO battle_action_scripts (fighter_id,script_selector,entry_ordinal,opcode_count) VALUES ($1,$2,$3,$4)", action.FighterId, action.ScriptSelector, action.Script.Entry, action.Script.Commands.Count);
        }
    }

    /// <summary>Executes a parameterised INSERT against the pack transaction.</summary>
    /// <param name="context">Current data-pack write context.</param>
    /// <param name="sql">SQL text with positional parameters.</param>
    /// <param name="values">Parameter values in source order.</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        }

        command.ExecuteNonQuery();
    }
}
