namespace Dw3.Formats.Battle;

/// <summary>
/// Draw state accumulated while walking a mesh command stream: the one-byte state flags, the
/// current texture command, the four colour slots and the UVs of the last textured polygon.
/// </summary>
internal sealed class MeshDrawState
{
    /// <summary>Colour every slot holds before the first colour command.</summary>
    private static readonly MeshColor DefaultColor = new(128, 128, 128);

    /// <summary>Gets a value indicating whether polygons carry four indices (command 0x8_, lo != 0).</summary>
    public bool Quad { get; private set; }

    /// <summary>Gets a value indicating whether polygons carry a UV payload (command 0x9_, lo != 0).</summary>
    public bool Textured { get; private set; }

    /// <summary>Gets a value indicating whether flag A is set (command 0xA_, lo != 0); kept verbatim.</summary>
    public bool FlagA { get; private set; }

    /// <summary>Gets a value indicating whether flag B is set (command 0xB_, lo != 0); kept verbatim.</summary>
    public bool FlagB { get; private set; }

    /// <summary>Gets a value indicating whether polygons carry normal indices (command 0xC_, lo != 0).</summary>
    public bool Lit { get; private set; }

    /// <summary>Gets a value indicating whether polygons are Gouraud-shaded (command 0xD_, lo != 0).</summary>
    public bool Gouraud { get; private set; }

    /// <summary>Gets the semi-transparency blend mode, the low nibble of the last 0xE_ command (0..15).</summary>
    public int Abr { get; private set; }

    /// <summary>Gets or sets the state of the last texture command (0x01), or null before the first one.</summary>
    public MeshTextureState? Texture { get; set; }

    /// <summary>Gets or sets the UVs of the last textured polygon, reused by untextured polygons; null before the first.</summary>
    public MeshUv[]? ResidualUvs { get; set; }

    /// <summary>Gets the four colour slots written by commands 0x02..0x05; slot 0 is the polygon flat colour.</summary>
    public MeshColor[] Colors { get; } = [DefaultColor, DefaultColor, DefaultColor, DefaultColor];

    /// <summary>Gets the number of vertex indices of the next polygon: 4 for quads, 3 otherwise.</summary>
    public int IndexCount => Quad ? 4 : 3;

    /// <summary>Applies a one-byte state command. Nibbles 0x1..0x7 and 0xF carry no state and are ignored.</summary>
    /// <param name="hi">High nibble of the command byte, 1..15.</param>
    /// <param name="lo">Low nibble of the command byte, the new value.</param>
    public void ApplyStateCommand(int hi, int lo)
    {
        switch (hi)
        {
            case 0x8:
                Quad = lo != 0;
                break;
            case 0x9:
                Textured = lo != 0;
                break;
            case 0xA:
                FlagA = lo != 0;
                break;
            case 0xB:
                FlagB = lo != 0;
                break;
            case 0xC:
                Lit = lo != 0;
                break;
            case 0xD:
                Gouraud = lo != 0;
                break;
            case 0xE:
                Abr = lo;
                break;
            default:
                break;
        }
    }
}
