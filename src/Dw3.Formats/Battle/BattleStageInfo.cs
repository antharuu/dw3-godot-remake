namespace Dw3.Formats.Battle;

/// <summary>
/// Parsed FightStageInfo record from file 0x1CB (FILE_FIGHT_STAGES, EU).
/// 27 records, each 0x58 bytes (sizeof(FightStageInfo) from stage.h).
/// </summary>
/// <param name="StageId">0-based stage index.</param>
/// <param name="ModelFile">Engine file id of the model archive.</param>
/// <param name="ModelEntry">Archive entry of the model within that file.</param>
/// <param name="MotionFile">Engine file id of the motion archive (usually same as ModelFile).</param>
/// <param name="MotionEntry">Archive entry of the motion data.</param>
/// <param name="Music">Music track id (may be -1).</param>
/// <param name="BgColor">Background clear colour (R, G, B bytes).</param>
/// <param name="NoBoundsBones">Terminated list of stage bones that disable source bounds checks.</param>
/// <param name="Lights">Three directional light entries from LightSet.</param>
/// <param name="Ambient">Ambient RGB as three s32 values read from stage.h SetBackColor args.</param>
public sealed record BattleStageInfo(
    int StageId,
    int ModelFile,
    int ModelEntry,
    int MotionFile,
    int MotionEntry,
    int Music,
    byte[] BgColor,
    byte[] NoBoundsBones,
    IReadOnlyList<BattleStageLightEntry> Lights,
    int[] Ambient);
