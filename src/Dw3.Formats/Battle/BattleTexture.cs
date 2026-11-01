namespace Dw3.Formats.Battle;

public sealed record BattleTexture(
    int Key, int Mode, int Page, int? ClutX, int? ClutY, int Width, int Height,
    int TransparentPixels, int StpPixels, byte[] Rgba);
