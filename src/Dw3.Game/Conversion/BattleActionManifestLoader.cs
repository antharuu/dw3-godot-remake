using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>Reads one validated action-script manifest reference from the prepared battle pack.</summary>
internal static class BattleActionManifestLoader
{
    /// <summary>Resolves one decoded action script through the pack database and its file resolver.</summary>
    /// <param name="database">Open data-pack database.</param>
    /// <param name="fighterId">Fighter whose script is requested.</param>
    /// <param name="scriptSelector">Technique script-selector that identifies the specific script.</param>
    /// <param name="requireFile">Pack-relative file validator.</param>
    /// <returns>The validated absolute action-script manifest path.</returns>
    /// <exception cref="CacheResourceException">The action is absent, unsupported, invalid, or missing its file.</exception>
    internal static string Load(SqliteConnection database, int fighterId, int scriptSelector, Func<string, string, string> requireFile)
    {
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT status, reason, manifest_path FROM battle_action_archives WHERE fighter_id=$id AND script_selector=$sel";
        command.Parameters.AddWithValue("$id", fighterId);
        command.Parameters.AddWithValue("$sel", scriptSelector);
        using SqliteDataReader row = command.ExecuteReader();
        if (!row.Read())
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"battle action script {fighterId}/{scriptSelector} is absent from the data pack");
        }

        return ReadDecodedRow(row, fighterId, scriptSelector, requireFile);
    }

    /// <summary>Validates the selected action row and rejects duplicates deterministically.</summary>
    /// <param name="row">Positioned action row.</param>
    /// <param name="fighterId">Fighter whose script is requested.</param>
    /// <param name="scriptSelector">Technique script-selector for error messages.</param>
    /// <param name="requireFile">Pack-relative file validator.</param>
    /// <returns>The validated absolute action-script manifest path.</returns>
    private static string ReadDecodedRow(SqliteDataReader row, int fighterId, int scriptSelector, Func<string, string, string> requireFile)
    {
        string status = row.GetString(0);
        if (!string.Equals(status, "decoded", StringComparison.Ordinal) || row.IsDBNull(2))
        {
            string reason = row.IsDBNull(1) ? "no reason recorded" : row.GetString(1);
            throw new CacheResourceException("CACHE_INVALID", $"battle action script {fighterId}/{scriptSelector} was not decoded: {reason}");
        }

        string path = requireFile(row.GetString(2), $"battle action script {fighterId}/{scriptSelector}");
        if (row.Read())
        {
            throw new CacheResourceException("CACHE_INVALID", $"battle action script {fighterId}/{scriptSelector} has duplicate records");
        }

        return path;
    }
}
