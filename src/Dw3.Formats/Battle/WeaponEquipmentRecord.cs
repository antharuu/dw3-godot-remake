namespace Dw3.Formats.Battle;

/// <summary>One 20-byte weapon equipment record.</summary>
public sealed record WeaponEquipmentRecord(
    short Charisma,
    byte EquipKind,
    byte Group,
    byte PartnerMask,
    ushort FirstStatAmount,
    ushort SecondStatAmount,
    ushort Attack,
    byte FirstStat,
    byte SecondStat,
    byte Accuracy,
    byte HitEffect,
    byte StatusChance,
    byte StatusPower,
    byte Family) : EquipmentRecord(Charisma, EquipKind, Group, PartnerMask);
