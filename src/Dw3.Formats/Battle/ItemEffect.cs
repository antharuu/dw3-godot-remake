namespace Dw3.Formats.Battle;

/// <summary>One four-byte item effect record addressed by an item record.</summary>
public sealed record ItemEffect(byte Flags, byte Kind, ushort Amount);
