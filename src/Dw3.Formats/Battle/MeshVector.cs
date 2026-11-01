namespace Dw3.Formats.Battle;

/// <summary>A signed 16-bit vector as stored in mesh vertex and normal streams.</summary>
public readonly record struct MeshVector(short X, short Y, short Z);
