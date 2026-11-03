using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>Reads combat manifests and assets exclusively from the validated extracted data pack.
/// JSON parsing is delegated to <see cref="BattleEncounterJsonParser"/> and
/// <see cref="BattleLoadoutJsonParser"/>; this class handles only file resolution and SQLite access.</summary>
public sealed class BattleDataPackLoader : IDisposable
{
    private readonly DataPackReader _pack;

    private readonly SqliteConnection _database;

    /// <summary>Initializes a new instance of the <see cref="BattleDataPackLoader"/> class bound to the given data pack.</summary>
    /// <param name="pack">The validated data pack reader.</param>
    public BattleDataPackLoader(DataPackReader pack)
    {
        _pack = pack;
        _database = pack.OpenDatabase();
    }

    /// <summary>Returns the absolute path to the 3D model JSON for the given fighter id, or throws if absent.</summary>
    /// <param name="fighterId">The numeric fighter id to look up.</param>
    /// <returns>The absolute path to the model JSON file.</returns>
    /// <exception cref="CacheResourceException">The record is absent, has the wrong status or is duplicated.</exception>
    public string ModelPath(int fighterId)
    {
        using SqliteCommand command = _database.CreateCommand();
        command.CommandText = "SELECT status, reason, manifest_path FROM battle_models WHERE fighter_id=$id";
        command.Parameters.AddWithValue("$id", fighterId);
        using SqliteDataReader row = command.ExecuteReader();
        if (!row.Read())
        {
            throw Missing($"battle model {fighterId} is absent from the data pack");
        }

        string status = row.GetString(0);
        if (!string.Equals(status, "decoded", StringComparison.Ordinal) || row.IsDBNull(2))
        {
            throw Invalid($"battle model {fighterId} was not decoded: {(row.IsDBNull(1) ? "no reason recorded" : row.GetString(1))}");
        }

        string path = RequireFile(row.GetString(2), $"battle model {fighterId}");
        if (row.Read())
        {
            throw Invalid($"battle model {fighterId} has duplicate records");
        }

        return path;
    }

    /// <summary>Returns the absolute path to the battle stage model JSON for the given stage id.</summary>
    /// <param name="stageId">The numeric battle stage id.</param>
    /// <returns>The absolute path to the stage model JSON.</returns>
    /// <exception cref="CacheResourceException">The file is absent from the data pack.</exception>
    public string StagePath(int stageId) => RequireFile($"battle/stages/stage-{stageId}-model.json", $"battle stage {stageId}");

    /// <summary>Loads and validates the encounter manifest from the data pack.</summary>
    /// <returns>The fully decoded <see cref="EncounterData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public EncounterData LoadEncounters()
    {
        try
        {
            using JsonDocument document = ReadJson("encounter/encounter.json", "encounter manifest");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-encounter-v1" || root.GetProperty("edition").GetString() != "SLES-03936")
            {
                throw new JsonException("encounter manifest schema mismatch");
            }

            return BattleEncounterJsonParser.ParseEncounters(root);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"encounter manifest invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the fighter manifest from the data pack.</summary>
    /// <returns>The fully decoded <see cref="FighterData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public FighterData LoadFighters()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            return BattleEncounterJsonParser.ParseFighters(document.RootElement);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"fighter data invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the digivolution manifest from the data pack.</summary>
    /// <returns>The fully decoded <see cref="DigivolutionData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public DigivolutionData LoadDigivolutions()
    {
        try
        {
            using JsonDocument document = ReadJson("digivolution/digivolution.json", "digivolution data");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m9b-digivolution-v2")
            {
                throw new JsonException("digivolution schema mismatch");
            }

            return BattleEncounterJsonParser.ParseDigivolutions(root);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"digivolution data invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the battle-usable items from the data pack.</summary>
    /// <returns>The fully decoded <see cref="BattleItemsData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public BattleItemsData LoadBattleItems()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            var names = LoadTextTable("battle/text/fsitmnam.json", "item names");
            return BattleLoadoutJsonParser.ParseBattleItems(document.RootElement, names);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"battle item data invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the battle rewards and growth tables from the data pack.</summary>
    /// <returns>The fully decoded <see cref="RewardsData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public RewardsData LoadRewards()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            return BattleEncounterJsonParser.ParseRewards(document.RootElement);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"battle reward data invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the battle text tables from the data pack.</summary>
    /// <returns>The fully decoded <see cref="BattleTextData"/>.</returns>
    /// <exception cref="CacheResourceException">A text file is absent or its JSON contract is violated.</exception>
    public BattleTextData LoadBattleText()
    {
        try
        {
            var menu = LoadTextTable("battle/text/fsmfight.json", "battle menu text");
            var names = LoadTextTable("battle/text/fsdignam.json", "Digimon names");
            var techniques = LoadTextTable("battle/text/fssklnam.json", "technique names");
            var skillInfo = LoadTextTable("battle/text/fssklinf.json", "technique descriptions");
            var itemInfo = LoadTextTable("battle/text/fsitminf.json", "item descriptions");
            var report = LoadTextTable("battle/text/fsfgtrpt.json", "battle report text");
            return BattleLoadoutJsonParser.ParseBattleText(menu, names, techniques, skillInfo, itemInfo, report);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"battle text data invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the battle HUD sprite frames from the data pack.</summary>
    /// <returns>The fully decoded <see cref="BattleHudData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public BattleHudData LoadBattleHud()
    {
        try
        {
            using JsonDocument document = ReadJson("battle/hud/hud.json", "battle HUD");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-battle-hud-v3")
            {
                throw new JsonException("battle HUD schema mismatch");
            }

            return BattleLoadoutJsonParser.ParseBattleHud(root, RequireFile);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"battle HUD invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the battle report sprites from the data pack.</summary>
    /// <returns>The fully decoded <see cref="BattleReportSprites"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public BattleReportSprites LoadBattleReportSprites()
    {
        try
        {
            using JsonDocument document = ReadJson("battle/report/report.json", "battle report sprites");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-battle-report-sprites-v1")
            {
                throw new JsonException("battle report schema mismatch");
            }

            return BattleLoadoutJsonParser.ParseBattleReportSprites(root, RequireFile);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"battle report sprites invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the battle menu icons from the data pack.</summary>
    /// <returns>The fully decoded <see cref="BattleMenuIconsData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public BattleMenuIconsData LoadBattleMenuIcons()
    {
        try
        {
            using JsonDocument document = ReadJson("battle/menu-icons/manifest.json", "battle menu icons");
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "m6-battle-menu-icons-v1")
            {
                throw new JsonException("battle menu icon schema mismatch");
            }

            return BattleLoadoutJsonParser.ParseBattleMenuIcons(root, RequireFile);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"battle menu icons invalid: {error.Message}");
        }
    }

    /// <summary>Loads and validates the technique data from the data pack.</summary>
    /// <returns>The fully decoded <see cref="TechniqueData"/>.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent or its JSON contract is violated.</exception>
    public TechniqueData LoadTechs()
    {
        try
        {
            using JsonDocument document = ReadBattleJson();
            return BattleLoadoutJsonParser.ParseTechs(document.RootElement);
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"technique data invalid: {error.Message}");
        }
    }

    /// <summary>Releases the underlying SQLite connection.</summary>
    public void Dispose() => _database.Dispose();

    /// <summary>Builds the error used when a required pack resource does not exist, so the host can tell the player to rerun extraction.</summary>
    /// <param name="message">Description of the missing resource.</param>
    /// <returns>An exception with code <c>CACHE_RESOURCE_UNAVAILABLE</c>, not yet thrown.</returns>
    private static CacheResourceException Missing(string message) => new("CACHE_RESOURCE_UNAVAILABLE", message);

    /// <summary>Builds the error used when a pack resource exists but its content cannot be trusted.</summary>
    /// <param name="message">Description of the defect.</param>
    /// <returns>An exception with code <c>CACHE_INVALID</c>, not yet thrown.</returns>
    private static CacheResourceException Invalid(string message) => new("CACHE_INVALID", message);

    /// <summary>Resolves a battle text table JSON inside the pack and parses it into token lines, one entry per message index.</summary>
    /// <param name="relativePath">Pack-relative path of the table, for example under <c>battle/</c>.</param>
    /// <param name="subject">Human-readable name used in the error message when the file is missing or malformed.</param>
    /// <returns>The parsed lines in file order. Never null.</returns>
    /// <exception cref="CacheResourceException">The file is missing, its path escapes the pack, or its content is invalid.</exception>
    private List<IReadOnlyList<TextToken>> LoadTextTable(string relativePath, string subject)
    {
        string path = RequireFile(relativePath, subject);
        return BattleLoadoutJsonParser.ParseTextTable(path, subject);
    }

    /// <summary>Opens <c>battle/battle.json</c> and checks that its <c>schema</c> field is <c>m9b-battle-v2</c> before handing it out, so a pack built by an older extractor is rejected instead of half-parsed.</summary>
    /// <returns>The parsed document, owned by the caller, who must dispose it.</returns>
    /// <exception cref="CacheResourceException">The file is missing or unreadable, or the schema tag does not match.</exception>
    private JsonDocument ReadBattleJson()
    {
        JsonDocument document = ReadJson("battle/battle.json", "battle data");
        if (document.RootElement.GetProperty("schema").GetString() != "m9b-battle-v2")
        {
            document.Dispose();
            throw Invalid("battle data schema mismatch");
        }

        return document;
    }

    /// <summary>Resolves a pack-relative JSON file and parses it, turning I/O and syntax failures into a cache error that names the subject.</summary>
    /// <param name="relativePath">Pack-relative path of the JSON file.</param>
    /// <param name="subject">Human-readable name used in error messages.</param>
    /// <returns>The parsed document, owned by the caller, who must dispose it.</returns>
    /// <exception cref="CacheResourceException">The file is missing, its path is rejected by the pack, or it cannot be read or parsed.</exception>
    private JsonDocument ReadJson(string relativePath, string subject)
    {
        string path = RequireFile(relativePath, subject);
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            throw Invalid($"{subject} is unreadable: {error.Message}");
        }
    }

    /// <summary>Maps a pack-relative path to an absolute file path and checks that the file exists.</summary>
    /// <param name="relativePath">Pack-relative path, rejected by the pack when it escapes the pack root.</param>
    /// <param name="subject">Human-readable name used in error messages.</param>
    /// <returns>An absolute path to an existing file.</returns>
    /// <exception cref="CacheResourceException"><c>CACHE_INVALID</c> when the path is rejected, <c>CACHE_RESOURCE_UNAVAILABLE</c> when the file does not exist.</exception>
    private string RequireFile(string relativePath, string subject)
    {
        string path;
        try
        {
            path = _pack.ResolvePath(relativePath);
        }
        catch (ArgumentException error)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{subject} path is invalid: {error.Message}");
        }

        if (!File.Exists(path))
        {
            throw Missing($"{subject} is missing: {path}");
        }

        return path;
    }
}
