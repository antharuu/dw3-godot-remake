using Microsoft.Data.Sqlite;

namespace Dw3.Data.Battle;

/// <summary>Additive version-one sprite-domain tables, independently validated from unrelated pack domains.</summary>
internal static class SpriteEffectSchema
{
    /// <summary>Creates empty sprite and source-script indices for prepared-domain publication.</summary>
    /// <param name="connection">Pack schema connection.</param>
    internal static void Create(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE battle_sprites_v1 (effect_id INTEGER PRIMARY KEY, status TEXT NOT NULL, payload TEXT NOT NULL);
            CREATE TABLE battle_sprite_scripts_v1 (fighter_id INTEGER NOT NULL, selector INTEGER NOT NULL, payload TEXT NOT NULL, PRIMARY KEY (fighter_id, selector));
            """;
        command.ExecuteNonQuery();
    }
}
