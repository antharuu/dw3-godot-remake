namespace Dw3.Formats.Battle;

/// <summary>Identifies a sprite sheet, its texture archive and source VRAM placement.</summary>
/// <param name="Index">Sheet registry index.</param>
/// <param name="TextureSelector">Packed texture archive selector, zero for resident common pages.</param>
/// <param name="SheetSelector">Packed sheet selector.</param>
/// <param name="Placement">Source VRAM X and Y coordinates stored as two signed 32-bit words.</param>
public sealed record SpriteEffectSheetReference(int Index, uint TextureSelector, uint SheetSelector, int[] Placement);
