using System.Text.Json;

namespace Dw3.Data.Battle;

/// <summary>Validates prepared sprite and screen-fade script dependencies independently of a renderer.</summary>
public static class SpriteEffectScriptReader
{
    /// <summary>Loads source signatures keyed by fighter and script selector.</summary>
    /// <param name="pack">Prepared pack only.</param>
    /// <returns>All source script outcomes, including unsupported records.</returns>
    public static IReadOnlyList<JsonElement> Load(DataPackReader pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        using var database = pack.OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT fighter_id,selector,payload FROM battle_sprite_scripts_v1 ORDER BY fighter_id,selector";
        using var rows = command.ExecuteReader();
        var results = new List<JsonElement>();
        while (rows.Read())
        {
            using JsonDocument document = JsonDocument.Parse(rows.GetString(2));
            JsonElement payload = document.RootElement;
            if (payload.GetProperty("schema").GetString() != "battle-sprite-scripts-v1"
                || payload.GetProperty("fighter").GetInt32() != rows.GetInt32(0)
                || payload.GetProperty("selector").GetInt32() != rows.GetInt32(1))
            {
                throw new InvalidDataException("Prepared sprite script identity or version is invalid.");
            }

            ValidateCommands(payload);
            results.Add(payload.Clone());
        }

        return results;
    }

    /// <summary>Rejects invalid fade signatures while preserving source command ordinals and durations.</summary>
    /// <param name="payload">One indexed source script.</param>
    private static void ValidateCommands(JsonElement payload)
    {
        if (payload.GetProperty("status").GetString() == "unsupported")
        {
            if (string.IsNullOrWhiteSpace(payload.GetProperty("reason").GetString()))
            {
                throw new InvalidDataException("Unsupported sprite script lacks qualification.");
            }

            return;
        }

        if (payload.GetProperty("status").GetString() != "decoded")
        {
            throw new InvalidDataException("Prepared sprite script status is invalid.");
        }

        foreach (JsonElement command in payload.GetProperty("commands").EnumerateArray().Where(c => c.GetProperty("opcode").GetInt32() == 7))
        {
            JsonElement args = command.GetProperty("arguments");
            if (args.GetArrayLength() != 2 || args[0].GetInt32() is < 0 or > 1 || args[1].GetInt32() < 0)
            {
                throw new InvalidDataException("Prepared screen-fade source signature is invalid.");
            }
        }
    }
}
