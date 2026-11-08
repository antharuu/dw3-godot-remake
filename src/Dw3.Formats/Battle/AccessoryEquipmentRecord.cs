namespace Dw3.Formats.Battle;

/// <summary>One 12-byte accessory equipment record.</summary>
public sealed record AccessoryEquipmentRecord(
    short Charisma,
    byte EquipKind,
    byte Group,
    byte PartnerMask,
    ushort Amount,
    byte Stat) : EquipmentRecord(Charisma, EquipKind, Group, PartnerMask);
