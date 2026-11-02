namespace Dw3.Formats.Battle;

/// <summary>
/// Dimensions and upload map of the local VRAM rebuilt from a model's texture archive.
/// The VRAM is <see cref="WidthWords"/> × <see cref="Height"/> 16-bit halfwords, with
/// each entry from the archive placed at its recorded column offset.
/// </summary>
/// <param name="WidthWords">Total width of the local VRAM in 16-bit halfwords.</param>
/// <param name="Height">Total height of the local VRAM in rows.</param>
/// <param name="Uploads">Ordered list of the individual TIM uploads that fill this VRAM.</param>
public sealed record BattleTextureVram(int WidthWords, int Height, IReadOnlyList<BattleTextureUpload> Uploads);
