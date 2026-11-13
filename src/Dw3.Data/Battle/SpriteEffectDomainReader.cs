using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dw3.Data.Battle;

/// <summary>Validates the prepared sprite index without any disc or extraction dependency.</summary>
public static class SpriteEffectDomainReader
{
    /// <summary>Loads every indexed identity and checks available frame files and sample references.</summary>
    /// <param name="pack">Prepared data pack reader.</param>
    /// <returns>Complete index including explicit unsupported and qualified absence records.</returns>
    public static IReadOnlyDictionary<int, PreparedSpriteEffect> Load(DataPackReader pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        using var database = pack.OpenDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT effect_id,status,payload FROM battle_sprites_v1 ORDER BY effect_id";
        using var rows = command.ExecuteReader();
        var results = new Dictionary<int, PreparedSpriteEffect>();
        while (rows.Read())
        {
            int id = rows.GetInt32(0);
            string status = rows.GetString(1);
            using JsonDocument document = JsonDocument.Parse(rows.GetString(2));
            JsonElement payload = document.RootElement;
            ValidateIdentity(id, status, payload);
            if (status == "available" || payload.TryGetProperty("frames", out _))
            {
                ValidateAvailable(pack, payload, status);
            }

            results.Add(id, new PreparedSpriteEffect(id, status, payload.Clone()));
        }

        if (results.Count == 0)
        {
            throw new InvalidDataException("Prepared sprite index is absent or empty.");
        }

        return results;
    }

    /// <summary>Rejects a missing index row rather than reporting source absence.</summary>
    /// <param name="index">Validated domain index.</param>
    /// <param name="id">Required script identity.</param>
    /// <returns>The exact indexed row.</returns>
    public static PreparedSpriteEffect Require(IReadOnlyDictionary<int, PreparedSpriteEffect> index, int id) =>
        index.TryGetValue(id, out PreparedSpriteEffect? effect) ? effect
            : throw new InvalidDataException($"Prepared sprite {id} is missing from the index.");

    /// <summary>Checks contract version and source identity for every status.</summary>
    /// <param name="id">SQLite identity.</param>
    /// <param name="status">Explicit availability state.</param>
    /// <param name="payload">Versioned metadata.</param>
    private static void ValidateIdentity(int id, string status, JsonElement payload)
    {
        if (id < 0 || payload.GetProperty("schema").GetString() != "battle-sprites-v2"
            || payload.GetProperty("reference").GetProperty("EffectId").GetInt32() != id
            || status is not ("available" or "unsupported" or "source-absent"))
        {
            throw new InvalidDataException("Prepared sprite identity, version or status is invalid.");
        }

        if (status != "available" && string.IsNullOrWhiteSpace(payload.GetProperty("reason").GetString()))
        {
            throw new InvalidDataException("Unavailable sprite lacks source qualification.");
        }
    }

    /// <summary>Checks all frame/palette identities and every animation's nine-channel samples.</summary>
    /// <param name="pack">Prepared image resolver.</param>
    /// <param name="payload">Available sprite contract.</param>
    /// <param name="status">Explicit asset availability.</param>
    private static void ValidateAvailable(DataPackReader pack, JsonElement payload, string status)
    {
        SpriteEffectPayloadValidator.Validate(payload);
        var keys = new HashSet<(int Frame, int Row)>();
        foreach (JsonElement frame in payload.GetProperty("frames").EnumerateArray())
        {
            if (!keys.Add((frame.GetProperty("frame").GetInt32(), frame.GetProperty("row").GetInt32())))
            {
                throw new InvalidDataException("Prepared sprite has a duplicate frame/palette identity.");
            }

            ValidateFrame(pack, frame);
        }

        ValidateUnsupportedFrames(payload, status, keys);
        if (keys.Count == 0 || payload.GetProperty("animations").GetArrayLength() == 0)
        {
            throw new InvalidDataException("Prepared sprite has no frames or animations.");
        }

        foreach (JsonElement animation in payload.GetProperty("animations").EnumerateArray().Where(a => a.ValueKind != JsonValueKind.Null))
        {
            ValidateAnimation(animation, keys);
        }
    }

    /// <summary>Keeps unresolved frame identities explicit without treating them as available images.</summary>
    /// <param name="payload">Decoded sprite source metadata.</param>
    /// <param name="status">Asset availability.</param>
    /// <param name="keys">Known frame/palette identities.</param>
    private static void ValidateUnsupportedFrames(JsonElement payload, string status, HashSet<(int Frame, int Row)> keys)
    {
        if (payload.GetProperty("unsupportedFrames").EnumerateArray().Any(frame => status == "available"
            || string.IsNullOrWhiteSpace(frame.GetProperty("reason").GetString())
            || !keys.Add((frame.GetProperty("frame").GetInt32(), frame.GetProperty("row").GetInt32()))))
        {
            throw new InvalidDataException("Prepared unresolved sprite frame qualification is invalid.");
        }
    }

    /// <summary>Checks source-time sample widths and references without filling missing frames.</summary>
    /// <param name="animation">One present source animation.</param>
    /// <param name="keys">Decoded frame/palette identities.</param>
    private static void ValidateAnimation(JsonElement animation, HashSet<(int Frame, int Row)> keys)
    {
        if (animation.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Prepared sprite animation has no duration.");
        }

        if (animation.EnumerateArray().Any(sample => sample.GetArrayLength() != 9 || !keys.Contains((sample[0].GetInt32(), sample[1].GetInt32()))))
        {
            throw new InvalidDataException("Prepared sprite sample width or frame/palette reference is invalid.");
        }
    }

    /// <summary>Checks the PNG hash, dimensions and retained source palette metadata.</summary>
    /// <param name="pack">Prepared image resolver.</param>
    /// <param name="frame">One frame/CLUT image contract.</param>
    private static void ValidateFrame(DataPackReader pack, JsonElement frame)
    {
        ValidateImage(pack, frame);
        foreach (JsonElement part in frame.GetProperty("parts").EnumerateArray())
        {
            ValidateImage(pack, part.GetProperty("layer"));
        }

        if (frame.GetProperty("parts").EnumerateArray().Any(part => part.GetProperty("palette").GetArrayLength() != 16 || part.GetProperty("clut").GetArrayLength() != 4
            || !IsQualifiedPaletteSource(part)))
        {
            throw new InvalidDataException("Prepared sprite palette metadata is invalid.");
        }
    }

    /// <summary>
    /// Accepts only the two qualified palette providers: the sheet's own loaded page, or the
    /// never-written VRAM rectangle whose sixteen words are zero.
    /// </summary>
    /// <param name="part">Prepared part contract.</param>
    /// <returns>Whether the provider, its packed VRAM address and its words agree.</returns>
    private static bool IsQualifiedPaletteSource(JsonElement part)
    {
        if (!part.TryGetProperty("vram", out JsonElement vram) || vram.GetArrayLength() != 2)
        {
            return false;
        }

        return part.TryGetProperty("paletteSource", out JsonElement source) && source.GetString() switch
        {
            "effect-page" => true,
            "cleared-vram" => vram[0].GetInt32() >= 448 && vram[0].GetInt32() + 16 <= 512 && vram[1].GetInt32() is >= 0 and < 256
                && part.GetProperty("palette").EnumerateArray().All(word => word.GetInt32() == 0),
            _ => false,
        };
    }

    /// <summary>Checks a prepared PNG and its registered image bounds.</summary>
    /// <param name="pack">Prepared path resolver.</param>
    /// <param name="frame">Frame or layer image contract.</param>
    private static void ValidateImage(DataPackReader pack, JsonElement frame)
    {
        string path = pack.ResolvePath(frame.GetProperty("path").GetString()!);
        byte[] png = File.Exists(path) ? File.ReadAllBytes(path) : [];
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(png)), frame.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Prepared sprite PNG is missing or corrupt.");
        }

        if (png.Length < 24 || BinaryPrimitives.ReadUInt32BigEndian(png) != 0x89504E47
            || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) != frame.GetProperty("width").GetInt32()
            || BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) != frame.GetProperty("height").GetInt32())
        {
            throw new InvalidDataException("Prepared sprite image bounds are invalid.");
        }
    }
}
