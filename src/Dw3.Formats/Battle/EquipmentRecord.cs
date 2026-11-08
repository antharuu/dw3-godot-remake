namespace Dw3.Formats.Battle;

/// <summary>Common source fields for an item which can be equipped by a partner.</summary>
public abstract record EquipmentRecord(short Charisma, byte EquipKind, byte Group, byte PartnerMask);
