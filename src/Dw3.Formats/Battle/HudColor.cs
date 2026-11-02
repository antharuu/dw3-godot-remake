namespace Dw3.Formats.Battle;

/// <summary>
/// Converts PS1 15-bit colour words (bits 0..4 red, 5..9 green, 10..14 blue, bit 15 STP) to
/// 8-bit RGBA channels for the battle HUD, report and talk-box sprites.
/// </summary>
public static class HudColor
{
    // PS1 5-bit channel expansion matching battle_hud.py: value * 255 // 31 (integer division).
    /// <summary>
    /// Expands one PS1 5-bit colour channel (0..31) to the nearest 8-bit equivalent (0..255),
    /// using the formula <c>value × 255 / 31</c> (integer division, matching the Python reference).
    /// </summary>
    /// <param name="value">5-bit channel value in the range 0..31.</param>
    /// <returns>Expanded 8-bit value in the range 0..255.</returns>
    public static byte Ps1Channel(int value) => (byte)(value * 255 / 31);

    /// <summary>Writes the expanded red, green and blue channels of <paramref name="color"/> at <paramref name="at"/>.</summary>
    /// <param name="rgba">RGBA destination buffer.</param>
    /// <param name="at">Byte offset of the pixel's red channel; alpha at <paramref name="at"/> + 3 is left untouched.</param>
    /// <param name="color">PS1 15-bit colour word.</param>
    public static void WriteRgb(byte[] rgba, int at, ushort color)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        rgba[at] = Ps1Channel(color & 31);
        rgba[at + 1] = Ps1Channel((color >> 5) & 31);
        rgba[at + 2] = Ps1Channel((color >> 10) & 31);
    }
}
