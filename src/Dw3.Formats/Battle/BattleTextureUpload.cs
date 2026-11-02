namespace Dw3.Formats.Battle;

/// <summary>
/// One TIM upload that was placed into the model's local VRAM during texture rebuilding.
/// The upload was positioned at column <see cref="XWords"/> in the VRAM halfword grid.
/// </summary>
/// <param name="XWords">Horizontal offset of the upload in the local VRAM, measured in 16-bit halfwords.</param>
/// <param name="WidthWords">Width of the uploaded image in 16-bit halfwords (not pixels).</param>
/// <param name="Height">Height of the uploaded image in rows.</param>
public sealed record BattleTextureUpload(int XWords, int WidthWords, int Height);
