using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>Resolves one prepared effect-model registry record while distinguishing a qualified source absence from an incomplete pack.</summary>
internal static class BattleEffectModelPathResolver
{
    /// <summary>Reads, validates and returns one effect-model manifest from the prepared registry.</summary>
    /// <param name="database">Open data-pack database containing the effect-model registry.</param>
    /// <param name="effectId">Script-visible shared effect-model id.</param>
    /// <param name="requireFile">Resolves a pack-relative manifest path and rejects missing files.</param>
    /// <returns>The validated manifest path, or null for a source-qualified absence.</returns>
    /// <exception cref="CacheResourceException">The record is absent without source qualification, corrupt, duplicated, or missing its manifest.</exception>
    internal static string? Resolve(SqliteConnection database, int effectId, Func<string, string, string> requireFile)
    {
        using SqliteCommand command = database.CreateCommand();
        command.CommandText = "SELECT manifest_path FROM battle_effect_models WHERE effect_id=$id";
        command.Parameters.AddWithValue("$id", effectId);
        using SqliteDataReader row = command.ExecuteReader();
        if (!row.Read())
        {
            return ResolveAbsent(effectId);
        }

        if (row.IsDBNull(0))
        {
            throw Invalid($"battle effect model {effectId} has no manifest path");
        }

        string path = requireFile(row.GetString(0), $"battle effect model {effectId}");
        BattleEffectModelManifestValidator.Validate(path, effectId);
        if (row.Read())
        {
            throw Invalid($"battle effect model {effectId} has duplicate records");
        }

        return path;
    }

    /// <summary>Returns the sole qualified source absence or rejects a missing prepared registry record.</summary>
    /// <param name="effectId">Script-visible effect-model id missing from the registry.</param>
    /// <returns>Null only when the source registry confirms absence.</returns>
    /// <exception cref="CacheResourceException">The missing id was not source-qualified as absent.</exception>
    private static string? ResolveAbsent(int effectId) => SupportedEffectModelAbsence.IsQualified(effectId)
        ? null
        : throw Missing($"battle effect model {effectId} is not source-qualified as absent");

    /// <summary>Builds the error used when a required pack resource does not exist.</summary>
    /// <param name="message">Description of the missing resource.</param>
    /// <returns>An exception with code <c>CACHE_RESOURCE_UNAVAILABLE</c>, not yet thrown.</returns>
    private static CacheResourceException Missing(string message) => new("CACHE_RESOURCE_UNAVAILABLE", message);

    /// <summary>Builds the error used when a pack resource exists but its content cannot be trusted.</summary>
    /// <param name="message">Description of the defect.</param>
    /// <returns>An exception with code <c>CACHE_INVALID</c>, not yet thrown.</returns>
    private static CacheResourceException Invalid(string message) => new("CACHE_INVALID", message);
}
