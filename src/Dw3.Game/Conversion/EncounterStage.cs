namespace Dw3Production.Conversion;

/// <summary>One stage's encounter registry: overlay identity plus its battle arrays.</summary>
public sealed record EncounterStage(string Overlay, string OverlaySha256, IReadOnlyList<EncounterStageArray> StageBattles);
