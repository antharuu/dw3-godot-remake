namespace Dw3.Formats.Text;

/// <summary>
/// Captures one decoded field-text token and the byte values carried by a control command.
/// </summary>
/// <param name="Kind">Token category selected from its leading byte.</param>
/// <param name="Code">Glyph code, icon index, or control command identifier.</param>
/// <param name="Args">Control-command argument bytes. Empty for glyphs and icons.</param>
public sealed record TextToken(TextTokenKind Kind, int Code, IReadOnlyList<int> Args);
