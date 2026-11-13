using System.Text.Json;

namespace Dw3Production.Conversion;

/// <summary>Converts one validated <c>battle-sprites-v2</c> payload into the runtime sheet contract.</summary>
internal static class BattleSpriteSheetParser
{
    /// <summary>Parses the source child animations and their frames.</summary>
    /// <param name="id">Script-visible sprite identity.</param>
    /// <param name="payload">Available prepared payload, already validated by the domain reader.</param>
    /// <param name="resolve">Maps a pack-relative layer path to an absolute file path.</param>
    /// <returns>The runtime sheet. Animations stop at the first empty archive entry, like the source task.</returns>
    /// <exception cref="FormatException">A sample references no decoded frame or the effect has no animation.</exception>
    public static BattleSpriteSheet Parse(int id, JsonElement payload, Func<string, string> resolve)
    {
        var animations = new List<IReadOnlyList<BattleSpriteSample>>();
        foreach (JsonElement animation in payload.GetProperty("animations").EnumerateArray())
        {
            if (animation.ValueKind == JsonValueKind.Null || animations.Count == BattleSpriteSheet.MaxAnimations)
            {
                break;
            }

            animations.Add([.. animation.EnumerateArray().Select(ReadSample)]);
        }

        var frames = payload.GetProperty("frames").EnumerateArray().Select(frame => ReadFrame(frame, resolve)).ToDictionary(frame => (frame.Frame, frame.Row));
        if (animations.Count == 0 || animations.Any(animation => animation.Count == 0 || animation.Any(sample => !frames.ContainsKey(sample.Key))))
        {
            throw new FormatException($"Prepared sprite {id} has no playable animation or references an undecoded frame.");
        }

        return new BattleSpriteSheet(id, animations, frames);
    }

    /// <summary>Reads one nine-channel sample.</summary>
    /// <param name="sample">Prepared sample array.</param>
    /// <returns>Typed sample.</returns>
    private static BattleSpriteSample ReadSample(JsonElement sample) =>
        BattleSpriteSample.From([.. sample.EnumerateArray().Select(channel => channel.GetInt16())]);

    /// <summary>Reads one frame and its placed layers in source placement order.</summary>
    /// <param name="frame">Prepared frame contract.</param>
    /// <param name="resolve">Pack path resolver.</param>
    /// <returns>Typed frame.</returns>
    private static BattleSpriteFrame ReadFrame(JsonElement frame, Func<string, string> resolve)
    {
        var parts = frame.GetProperty("parts").EnumerateArray().Select(part =>
        {
            JsonElement layer = part.GetProperty("layer");
            return new BattleSpritePart(
                resolve(layer.GetProperty("path").GetString() ?? throw new FormatException("Sprite layer has no path.")),
                part.GetProperty("dx").GetInt32(),
                part.GetProperty("dy").GetInt32(),
                layer.GetProperty("width").GetInt32(),
                layer.GetProperty("height").GetInt32(),
                part.GetProperty("paletteSource").GetString() ?? string.Empty);
        }).ToArray();
        return new BattleSpriteFrame(frame.GetProperty("frame").GetInt32(), frame.GetProperty("row").GetInt32(), frame.GetProperty("abr").GetInt32(), parts);
    }
}
