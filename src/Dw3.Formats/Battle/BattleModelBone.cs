namespace Dw3.Formats.Battle;

/// <summary>One bone of a model hierarchy. The root bone (index 0) has no parent, mesh or key track.</summary>
/// <param name="Parent">Index of an earlier bone.</param>
/// <param name="MeshEntry">Archive entry of the bone's mesh.</param>
/// <param name="KeyEntry">Archive entry of the bone's key tracks.</param>
public sealed record BattleModelBone(int Index, int? Parent, int? MeshEntry, int? KeyEntry);
