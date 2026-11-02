using Dw3.Data;
using Dw3.Formats.Encounter;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// Persists decoded encounter tables and per-stage battle arrays in the data pack database.
/// </summary>
internal static class EncounterSqlWriter
{
    /// <summary>
    /// Inserts every encounter-domain row in the historic write order.
    /// </summary>
    /// <param name="context">Provides the target database and transaction.</param>
    /// <param name="enemies">Provides decoded enemy records in source order.</param>
    /// <param name="encounters">Provides decoded encounter records in source order.</param>
    /// <param name="battleRates">Provides battle rates in source order.</param>
    /// <param name="stages">Maps stage identifiers to decoded stage arrays.</param>
    internal static void Write(
        DataPackWriteContext context,
        IReadOnlyList<BattleEnemy> enemies,
        IReadOnlyList<EncounterRecord> encounters,
        IReadOnlyList<int> battleRates,
        IReadOnlyDictionary<int, EncounterStageData> stages)
    {
        WriteEnemies(context, enemies);
        WriteEncounters(context, encounters);
        WriteBattleRates(context, battleRates);
        WriteStageArrays(context, stages);
    }

    /// <summary>
    /// Inserts one row per enemy record into <c>encounter_enemies</c> with index, fighter,
    /// level, HP, MP, and strength (all cast to int from their unsigned disc representation).
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="enemies">The decoded enemy records in source order.</param>
    private static void WriteEnemies(DataPackWriteContext context, IReadOnlyList<BattleEnemy> enemies)
    {
        foreach (BattleEnemy enemy in enemies)
        {
            Insert(
                context,
                "INSERT INTO encounter_enemies (enemy_index,fighter,level,hp,mp,strength) VALUES ($1,$2,$3,$4,$5,$6)",
                enemy.Index,
                enemy.Fighter,
                (int)enemy.Level,
                (int)enemy.Hp,
                (int)enemy.Mp,
                (int)enemy.Strength);
        }
    }

    /// <summary>
    /// Inserts one row per encounter record into <c>encounter_records</c> with enemy indices,
    /// ambush chance, kind, and a JSON-encoded blocks array.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="encounters">The decoded encounter records in source order.</param>
    private static void WriteEncounters(DataPackWriteContext context, IReadOnlyList<EncounterRecord> encounters)
    {
        foreach (EncounterRecord encounter in encounters)
        {
            string blocksJson = "[" + string.Join(",", encounter.Blocks) + "]";
            Insert(
                context,
                "INSERT INTO encounter_records (encounter_index,enemy0,enemy1,enemy2,ambush_chance,kind,blocks) VALUES ($1,$2,$3,$4,$5,$6,$7)",
                encounter.Index,
                encounter.EnemyIndices[0],
                encounter.EnemyIndices[1],
                encounter.EnemyIndices[2],
                (int)encounter.AmbushChance,
                (int)encounter.Kind,
                blocksJson);
        }
    }

    /// <summary>
    /// Inserts one row per rate index into <c>encounter_battle_rates</c>.
    /// Each rate is the probability (per step) of triggering a random encounter in the corresponding field zone.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="battleRates">The ordered battle rate values in source order.</param>
    private static void WriteBattleRates(DataPackWriteContext context, IReadOnlyList<int> battleRates)
    {
        for (int index = 0; index < battleRates.Count; index++)
        {
            Insert(
                context,
                "INSERT INTO encounter_battle_rates (rate_index,rate) VALUES ($1,$2)",
                index,
                battleRates[index]);
        }
    }

    /// <summary>
    /// Inserts one row per stage battle array into <c>encounter_stage_arrays</c> with the overlay
    /// RAM address, entry count, and overlay SHA-256 for provenance.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="stages">Maps stage identifiers to their decoded stage array data.</param>
    private static void WriteStageArrays(
        DataPackWriteContext context,
        IReadOnlyDictionary<int, EncounterStageData> stages)
    {
        foreach ((int stageId, EncounterStageData stageData) in stages)
        {
            for (int arrayIndex = 0; arrayIndex < stageData.Arrays.Count; arrayIndex++)
            {
                StageBattleArray array = stageData.Arrays[arrayIndex];
                Insert(
                    context,
                    "INSERT INTO encounter_stage_arrays (stage_id,array_index,addr,entry_count,overlay_sha256) VALUES ($1,$2,$3,$4,$5)",
                    stageId,
                    arrayIndex,
                    unchecked((int)array.Address),
                    array.Count,
                    stageData.OverlaySha256);
            }
        }
    }

    /// <summary>
    /// Executes a parameterised INSERT command against the current pack transaction.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="sql">The parameterised SQL text using positional parameters ($1, $2, …).</param>
    /// <param name="values">The parameter values in positional order.</param>
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
