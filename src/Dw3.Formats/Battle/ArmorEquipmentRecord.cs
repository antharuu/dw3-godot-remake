namespace Dw3.Formats.Battle;

/// <summary>One 20-byte armour equipment record.</summary>
public sealed record ArmorEquipmentRecord(
    short Charisma,
    byte EquipKind,
    byte Group,
    byte PartnerMask,
    ushort FirstStatAmount,
    ushort SecondStatAmount,
    byte FirstStat,
    byte SecondStat,
    short Defense,
    byte Evasion) : EquipmentRecord(Charisma, EquipKind, Group, PartnerMask);
