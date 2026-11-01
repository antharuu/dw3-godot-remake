namespace Dw3.Formats.Field;

/// <summary>An inclusive progress-value range used by snapshot visibility conditions.</summary>
public sealed record NpcProgressRange(byte Minimum, byte Maximum);
