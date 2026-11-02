namespace Dw3Production.Conversion;

/// <summary>One icon animation table: its steps and the step index its loop sentinel jumps to.</summary>
public sealed record PlayerIconAnimation(int Substate, IReadOnlyList<PlayerIconStep> Steps, int LoopTarget);
