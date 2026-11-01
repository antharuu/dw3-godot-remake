namespace Dw3.Formats.Battle;

public sealed record BattleTextureVram(int WidthWords, int Height, IReadOnlyList<BattleTextureUpload> Uploads);
