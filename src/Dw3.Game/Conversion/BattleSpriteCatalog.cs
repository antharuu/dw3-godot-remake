using System.Text.Json;
using Dw3.Data;
using Dw3.Data.Battle;
using Microsoft.Data.Sqlite;

namespace Dw3Production.Conversion;

/// <summary>
/// The validated prepared action-sprite domain, read once from the pack (never from the disc) and
/// parsed into runtime sheets on demand.
/// </summary>
public sealed class BattleSpriteCatalog
{
    private readonly IReadOnlyDictionary<int, PreparedSpriteEffect> _index;
    private readonly Func<string, string> _resolve;
    private readonly Dictionary<int, BattleSpriteResolution> _resolved = [];

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteCatalog"/> class.</summary>
    /// <param name="index">Validated prepared index.</param>
    /// <param name="resolve">Maps a pack-relative layer path to an absolute path.</param>
    public BattleSpriteCatalog(IReadOnlyDictionary<int, PreparedSpriteEffect> index, Func<string, string> resolve)
    {
        _index = index;
        _resolve = resolve;
    }

    /// <summary>Gets the validated prepared index, including unavailable rows.</summary>
    public IReadOnlyDictionary<int, PreparedSpriteEffect> Index => _index;

    /// <summary>Loads and validates the sprite domain through the production reader.</summary>
    /// <param name="pack">Prepared pack.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="CacheResourceException">The domain is absent, empty, of another version or corrupt.</exception>
    public static BattleSpriteCatalog Load(DataPackReader pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        try
        {
            return new BattleSpriteCatalog(SpriteEffectDomainReader.Load(pack), pack.ResolvePath);
        }
        catch (Exception error) when (error is InvalidDataException or SqliteException or JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"battle sprite domain unavailable: {error.Message}");
        }
    }

    /// <summary>Resolves one identity, parsing an available sheet once.</summary>
    /// <param name="id">Script-visible identity.</param>
    /// <returns>Explicit availability with the sheet or the source qualification.</returns>
    /// <exception cref="CacheResourceException">An available row cannot be parsed into a playable sheet.</exception>
    public BattleSpriteResolution Resolve(int id)
    {
        if (_resolved.TryGetValue(id, out BattleSpriteResolution? cached))
        {
            return cached;
        }

        BattleSpriteResolution resolution = Build(id);
        _resolved.Add(id, resolution);
        return resolution;
    }

    /// <summary>Builds a resolution from the indexed row.</summary>
    /// <param name="id">Script-visible identity.</param>
    /// <returns>The uncached resolution.</returns>
    private BattleSpriteResolution Build(int id)
    {
        if (!_index.TryGetValue(id, out PreparedSpriteEffect? row))
        {
            return new BattleSpriteResolution(id, BattleSpriteAvailability.Missing, null, "identity absent from the prepared sprite index");
        }

        string? reason = row.Payload.TryGetProperty("reason", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return row.Status switch
        {
            "available" => new BattleSpriteResolution(id, BattleSpriteAvailability.Available, ParseAvailable(id, row.Payload), null),
            "source-absent" => new BattleSpriteResolution(id, BattleSpriteAvailability.SourceAbsent, null, reason),
            _ => new BattleSpriteResolution(id, BattleSpriteAvailability.Unsupported, null, reason),
        };
    }

    /// <summary>Parses an available payload, reporting a corrupt contract as a cache error.</summary>
    /// <param name="id">Script-visible identity.</param>
    /// <param name="payload">Validated payload.</param>
    /// <returns>Renderable sheet.</returns>
    private BattleSpriteSheet ParseAvailable(int id, JsonElement payload)
    {
        try
        {
            return BattleSpriteSheetParser.Parse(id, payload, _resolve);
        }
        catch (Exception error) when (error is FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new CacheResourceException("CACHE_INVALID", $"battle sprite {id} is not playable: {error.Message}");
        }
    }
}
