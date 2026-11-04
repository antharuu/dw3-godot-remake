namespace Dw3Production.Presentation;

/// <summary>Provides the source-order and per-texel blend contract for observed fighter primitives.</summary>
public static class BattlePrimitiveCompositor
{
    /// <summary>Orders source primitives from far to near and reverses their submission order within equal keys.</summary>
    /// <param name="primitives">Unsplit source primitives in command-stream submission order.</param>
    /// <returns>Indices into <paramref name="primitives"/> in compositor order.</returns>
    public static int[] Order(IReadOnlyList<SourcePrimitive> primitives) =>
        [.. Enumerable.Range(0, primitives.Count)
            .OrderByDescending(index => primitives[index].Key)
            .ThenByDescending(index => index)];

    /// <summary>Returns the triangles emitted after a source primitive has been ordered as one unit.</summary>
    /// <param name="primitive">The source triangle or quad to expand.</param>
    /// <returns>Three indices for a triangle or the stable six-index quad split.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The primitive is neither a triangle nor a quad.</exception>
    public static ReadOnlySpan<int> EmitIndices(SourcePrimitive primitive) => primitive.CornerCount switch
    {
        3 => [0, 1, 2],
        4 => [0, 1, 2, 2, 1, 3],
        _ => throw new ArgumentOutOfRangeException(nameof(primitive), primitive.CornerCount, "source primitive must have three or four corners"),
    };

    /// <summary>Composites one decoded texel over the current framebuffer colour.</summary>
    /// <param name="background">Colour already written by farther primitives.</param>
    /// <param name="texel">Palette-decoded source texel and its STP state.</param>
    /// <param name="mode">The source primitive's observed ABR mode.</param>
    /// <returns>The resulting framebuffer colour.</returns>
    /// <exception cref="NotSupportedException">The mode is not observed in the fighter corpus.</exception>
    public static Ps1CompositorColor Composite(Ps1CompositorColor background, Ps1CompositorTexel texel, Ps1AbrMode mode)
    {
        if (texel.Transparent)
        {
            return background;
        }

        var foreground = new Ps1CompositorColor(texel.Red, texel.Green, texel.Blue);
        if (!texel.Stp || mode == Ps1AbrMode.Opaque)
        {
            return foreground;
        }

        return mode switch
        {
            Ps1AbrMode.HalfBackgroundHalfForeground => Half(background, foreground),
            Ps1AbrMode.Additive => Add(background, foreground),
            _ => throw new NotSupportedException($"PS1 ABR mode {(int)mode} is not observed in the fighter corpus"),
        };
    }

    /// <summary>Averages every background and foreground component without changing non-STP texels.</summary>
    /// <param name="background">Colour already written by farther primitives.</param>
    /// <param name="foreground">Current STP foreground colour.</param>
    /// <returns>The ABR zero blend result.</returns>
    private static Ps1CompositorColor Half(Ps1CompositorColor background, Ps1CompositorColor foreground) =>
        new(
            (byte)((background.Red + foreground.Red) / 2),
            (byte)((background.Green + foreground.Green) / 2),
            (byte)((background.Blue + foreground.Blue) / 2));

    /// <summary>Adds every background and foreground component with source-domain saturation.</summary>
    /// <param name="background">Colour already written by farther primitives.</param>
    /// <param name="foreground">Current STP foreground colour.</param>
    /// <returns>The ABR one blend result.</returns>
    private static Ps1CompositorColor Add(Ps1CompositorColor background, Ps1CompositorColor foreground) =>
        new(AddComponent(background.Red, foreground.Red), AddComponent(background.Green, foreground.Green), AddComponent(background.Blue, foreground.Blue));

    /// <summary>Adds two eight-bit components without wrapping.</summary>
    /// <param name="background">Background component.</param>
    /// <param name="foreground">Foreground component.</param>
    /// <returns>The saturated component.</returns>
    private static byte AddComponent(byte background, byte foreground) => (byte)Math.Min(byte.MaxValue, background + foreground);
}
