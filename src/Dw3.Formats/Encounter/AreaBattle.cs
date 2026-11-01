namespace Dw3.Formats.Encounter;

/// <summary>
/// One battle within a BattleList: encounter index, fight stage, and music identifier.
/// Decoded from 12-byte AreaBattle records in WSTAG*.PRO overlays.
/// </summary>
public sealed record AreaBattle(int Encounter, int Stage, int Music, string MusicName);
