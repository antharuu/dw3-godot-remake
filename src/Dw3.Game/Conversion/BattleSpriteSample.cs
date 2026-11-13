using Godot;

namespace Dw3Production.Conversion;

/// <summary>
/// One source-time sample of a two-dimensional effect animation, in the nine prepared channels:
/// frame, animated CLUT row, screen offset, fixed-point scale (4096 = 1) and rotation (4096 = one turn).
/// </summary>
/// <param name="Frame">Source sheet frame identity.</param>
/// <param name="Row">Animated CLUT row added to the frame and part rows.</param>
/// <param name="X">Horizontal offset in source screen pixels.</param>
/// <param name="Y">Vertical offset in source screen pixels.</param>
/// <param name="ScaleX">Horizontal fixed-point scale.</param>
/// <param name="ScaleY">Vertical fixed-point scale.</param>
/// <param name="RotationX">Rotation about the screen X axis.</param>
/// <param name="RotationY">Rotation about the screen Y axis.</param>
/// <param name="RotationZ">Rotation in the screen plane.</param>
public readonly record struct BattleSpriteSample(short Frame, short Row, short X, short Y, short ScaleX, short ScaleY, short RotationX, short RotationY, short RotationZ)
{
    /// <summary>One fixed-point unit.</summary>
    private const float One = 4096f;

    /// <summary>Gets a value indicating whether the source registers a draw callback for this sample (both scales nonzero).</summary>
    public bool IsDrawn => ScaleX != 0 && ScaleY != 0;

    /// <summary>Gets the frame and palette identity referenced by this sample.</summary>
    public (int Frame, int Row) Key => (Frame, Row);

    /// <summary>Builds a sample from one validated nine-channel prepared row.</summary>
    /// <param name="channels">Frame, row, x, y, scale x, scale y, rotation x, y and z.</param>
    /// <returns>The typed sample.</returns>
    /// <exception cref="FormatException">The row does not hold nine channels.</exception>
    public static BattleSpriteSample From(IReadOnlyList<short> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count != 9)
        {
            throw new FormatException("Sprite sample must hold nine channels.");
        }

        return new BattleSpriteSample(channels[0], channels[1], channels[2], channels[3], channels[4], channels[5], channels[6], channels[7], channels[8]);
    }

    /// <summary>
    /// Returns the screen-plane part of the source drawer matrix: the YXZ rotation matrix
    /// (mY * mX * mZ) with its columns scaled, applied to placements whose depth is zero.
    /// </summary>
    /// <returns>The linear transform around the sprite pivot, without translation.</returns>
    public Transform2D LinearTransform()
    {
        float x = RotationX * Mathf.Tau / One;
        float y = RotationY * Mathf.Tau / One;
        float z = RotationZ * Mathf.Tau / One;
        float sx = ScaleX / One;
        float sy = ScaleY / One;
        float cosX = Mathf.Cos(x);
        float sinX = Mathf.Sin(x);
        float cosY = Mathf.Cos(y);
        float sinY = Mathf.Sin(y);
        float cosZ = Mathf.Cos(z);
        float sinZ = Mathf.Sin(z);
        var column0 = new Vector2((cosY * cosZ) + (sinY * sinX * sinZ), cosX * sinZ) * sx;
        var column1 = new Vector2((sinY * sinX * cosZ) - (cosY * sinZ), cosX * cosZ) * sy;
        return new Transform2D(column0, column1, Vector2.Zero);
    }
}
