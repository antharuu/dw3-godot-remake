namespace Dw3.Formats.Battle;

/// <summary>Identifies one source 2D sprite effect and its archive selector.</summary>
/// <param name="EffectId">The script-visible 2D effect identity.</param>
/// <param name="SheetIndex">The source sheet registry index.</param>
/// <param name="AnimationFileId">The engine file id of the effect animation archive.</param>
/// <param name="AnimationArchiveEntry">The source archive entry containing the effect animation list.</param>
public sealed record SpriteEffectReference(int EffectId, int SheetIndex, int AnimationFileId, int AnimationArchiveEntry);
