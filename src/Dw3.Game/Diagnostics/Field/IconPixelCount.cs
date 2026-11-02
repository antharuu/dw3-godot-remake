using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Icon texels found at their expected viewport pixel, with the red and blue ones counted apart.</summary>
/// <param name="Matched">Opaque texels whose viewport pixel has the texel colour.</param>
/// <param name="Red">Matched texels that are clearly red: R above 150, G and B below 120 on the 0-255 scale.</param>
/// <param name="Blue">Matched texels that are clearly blue: B above 150, R below 120 on the 0-255 scale.</param>
internal sealed record IconPixelCount(int Matched, int Red, int Blue)
{
    /// <summary>Counts one more matched texel.</summary>
    /// <param name="texel">Colour of the matched texel.</param>
    /// <returns>The updated counts.</returns>
    public IconPixelCount Add(Color texel)
    {
        if (texel.R8 > 150 && texel.G8 < 120 && texel.B8 < 120)
        {
            return new IconPixelCount(Matched + 1, Red + 1, Blue);
        }

        var blue = texel.B8 > 150 && texel.R8 < 120 ? 1 : 0;
        return new IconPixelCount(Matched + 1, Red, Blue + blue);
    }
}
