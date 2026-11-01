namespace Dw3.Formats.Field;

/// <summary>Known constant values in the four standard MIPS argument registers.</summary>
public sealed record MipsCallArguments(uint? A0, uint? A1, uint? A2, uint? A3);
