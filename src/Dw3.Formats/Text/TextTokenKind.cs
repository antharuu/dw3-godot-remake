namespace Dw3.Formats.Text;

/// <summary>
/// Distinguishes a printable glyph, inline icon, or control sequence in a field text entry.
/// </summary>
public enum TextTokenKind
{
    /// <summary>Glyph code decoded from a byte in the printable range.</summary>
    Glyph,

    /// <summary>Icon index following the icon marker byte.</summary>
    Icon,

    /// <summary>Control command and its argument bytes.</summary>
    Control,
}
