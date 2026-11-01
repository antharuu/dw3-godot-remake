namespace Dw3.Formats.Battle;

/// <summary>One fixed-size partner creature record from the boot executable.</summary>
public sealed record DigimonRecord(
    ushort Id,
    IReadOnlyList<ushort> BattleStats,
    IReadOnlyList<ushort> Resistances,
    IReadOnlyList<ushort> Skills,
    ushort PairTechnique,
    IReadOnlyList<byte> StatusResistances,
    IReadOnlyList<byte> SkillLevels,
    IReadOnlyList<byte> KnownLevels,
    byte ExperienceLevel,
    byte PairPartner,
    byte ExperienceRate,
    byte Hp,
    byte Mp,
    byte HpGrowth,
    byte MpGrowth,
    IReadOnlyList<byte> StatGrowth,
    IReadOnlyList<byte> ResistGrowth,
    IReadOnlyList<byte> BlastForms,
    byte NameId,
    byte Family);
