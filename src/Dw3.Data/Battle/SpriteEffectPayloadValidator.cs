using System.Text.Json;

namespace Dw3.Data.Battle;

/// <summary>Checks prepared source geometry and palette-independent texture storage.</summary>
internal static class SpriteEffectPayloadValidator
{
    /// <summary>Validates sheet identity, decoded texture dimensions and source part references.</summary>
    /// <param name="payload">Decoded sprite contract.</param>
    internal static void Validate(JsonElement payload)
    {
        JsonElement sheet = payload.GetProperty("sheet");
        if (sheet.GetProperty("Index").GetInt32() != payload.GetProperty("reference").GetProperty("SheetIndex").GetInt32()
            || sheet.GetProperty("Placement").GetArrayLength() != 2)
        {
            throw new InvalidDataException("Prepared sprite sheet identity or placement width is invalid.");
        }

        if (payload.GetProperty("textureWords").EnumerateArray().Any(page => page.GetArrayLength() != 256 || page.EnumerateArray().Any(row => row.GetArrayLength() != 64)))
        {
            throw new InvalidDataException("Prepared sprite texture word page is malformed.");
        }

        int count = payload.GetProperty("parts").GetArrayLength();
        foreach (JsonElement frame in payload.GetProperty("sourceFrames").EnumerateArray())
        {
            ValidatePlacements(frame.GetProperty("placements"), count);
        }
    }

    /// <summary>Checks three-word source placements against their distinct part index space.</summary>
    /// <param name="placements">Source-order part index and pivot words.</param>
    /// <param name="partCount">Number of texture-part definitions.</param>
    private static void ValidatePlacements(JsonElement placements, int partCount)
    {
        if (placements.GetArrayLength() == 0 || placements.EnumerateArray().Any(p => p.GetArrayLength() != 3 || p[0].GetInt32() < 0 || p[0].GetInt32() >= partCount))
        {
            throw new InvalidDataException("Prepared sprite placement references an absent part.");
        }
    }
}
