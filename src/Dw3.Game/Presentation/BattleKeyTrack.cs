using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Fixed-point sampling of one bone key channel. A key is stored as a <see cref="Vector4"/> whose
/// X/Y/Z hold the integer channel value (position units, rotation in 4096ths of a turn, or scale
/// in 4096ths) and whose W holds the key's frame number.
/// </summary>
/// <remarks>
/// Interpolation uses a 12-bit factor and signed integer products, never a float lerp: bone
/// visibility crosses its scale threshold (48) on this exact fixed-point result.
/// </remarks>
internal static class BattleKeyTrack
{
    /// <summary>Samples a channel at a frame, holding the first and last keys outside their range.</summary>
    /// <param name="keys">Keys sorted by frame.</param>
    /// <param name="frame">Source animation frame.</param>
    /// <returns>The channel value, or zero for an empty channel.</returns>
    public static Vector3 Sample(List<Vector4> keys, int frame)
    {
        if (keys.Count == 0)
        {
            return Vector3.Zero;
        }

        if (frame <= keys[0].W)
        {
            return Value(keys[0]);
        }

        for (var i = 1; i < keys.Count; i++)
        {
            if (frame <= keys[i].W)
            {
                return Interpolate(keys[i - 1], keys[i], frame);
            }
        }

        return Value(keys[^1]);
    }

    /// <summary>Finds the value of the first key at or after a frame, as a blend target.</summary>
    /// <param name="keys">Non-empty keys sorted by frame.</param>
    /// <param name="frame">Source animation frame.</param>
    /// <returns>That key's value, or the last key's value when every key is earlier.</returns>
    public static Vector3 FirstKeyAtOrAfter(List<Vector4> keys, int frame)
    {
        foreach (var key in keys)
        {
            if (key.W >= frame)
            {
                return Value(key);
            }
        }

        return Value(keys[^1]);
    }

    /// <summary>Blends two channel values by a 12-bit fixed-point weight.</summary>
    /// <param name="from">Value at weight 0.</param>
    /// <param name="to">Value at weight 4096.</param>
    /// <param name="blend">Weight in 4096ths.</param>
    /// <returns>The integer-truncated blend.</returns>
    public static Vector3 FixedBlend(Vector3 from, Vector3 to, int blend) => new(
        (int)from.X + ((((int)to.X - (int)from.X) * blend) >> 12),
        (int)from.Y + ((((int)to.Y - (int)from.Y) * blend) >> 12),
        (int)from.Z + ((((int)to.Z - (int)from.Z) * blend) >> 12));

    /// <summary>Tells whether any component of a scale is zero, which disables scale blending.</summary>
    /// <param name="value">Fixed-point scale.</param>
    /// <returns>True when X, Y or Z is zero.</returns>
    public static bool HasZero(Vector3 value) => value.X == 0 || value.Y == 0 || value.Z == 0;

    private static Vector3 Value(Vector4 key) => new(key.X, key.Y, key.Z);

    private static Vector3 Interpolate(Vector4 a, Vector4 b, int frame)
    {
        if (frame == b.W)
        {
            return Value(b);
        }

        var factor = ((frame - (int)a.W) << 12) / ((int)b.W - (int)a.W);
        return new Vector3(
            (int)a.X + ((((int)b.X - (int)a.X) * factor) >> 12),
            (int)a.Y + ((((int)b.Y - (int)a.Y) * factor) >> 12),
            (int)a.Z + ((((int)b.Z - (int)a.Z) * factor) >> 12));
    }
}
