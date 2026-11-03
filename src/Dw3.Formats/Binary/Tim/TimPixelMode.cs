namespace Dw3.Formats.Binary.Tim;

/// <summary>PSX TIM pixel modes supported by this decoder.</summary>
public enum TimPixelMode
{
    /// <summary>
    /// 4-bit indexed colour.
    /// CLUT has 16 entries; pixels are packed two per byte (low nibble = left pixel).
    /// Words-per-line in the image block must be multiplied by 4 to get pixel width.
    /// TIM flags field = 0x08.
    /// </summary>
    Indexed4Bit,

    /// <summary>
    /// 8-bit indexed colour.
    /// CLUT has 256 entries; one byte per pixel.
    /// Words-per-line must be multiplied by 2 to get pixel width.
    /// TIM flags field = 0x09.
    /// </summary>
    Indexed8Bit,

    /// <summary>
    /// 15-bit direct colour (5-5-5 BGR), one 16-bit word per pixel.
    /// No CLUT.  Words-per-line equals pixel width.
    /// TIM flags field = 0x02.
    /// </summary>
    Direct15Bit,
}
