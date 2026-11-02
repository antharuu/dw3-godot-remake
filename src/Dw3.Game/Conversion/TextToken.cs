namespace Dw3Production.Conversion;

/// <summary>One decoded text token: a glyph (t=g, code 4..233), an icon (t=i, index 1..114)
/// or a control (t=c, command 0..9 with its arguments).</summary>
public sealed record TextToken(string Type, int Code, int Command, IReadOnlyList<int> Args)
{
    /// <summary>Gets a value indicating whether this token is a glyph (t=g).</summary>
    public bool IsGlyph => Type == "g";

    /// <summary>Gets a value indicating whether this token is an icon (t=i).</summary>
    public bool IsIcon => Type == "i";

    /// <summary>Gets a value indicating whether this token is a control (t=c).</summary>
    public bool IsControl => Type == "c";
}
