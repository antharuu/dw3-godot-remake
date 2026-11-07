namespace Dw3Production.Simulation;

/// <summary>The resolved outcome of one damaging technique, including its hit check and calculated damage.</summary>
/// <param name="Landed">Whether the target takes the calculated damage and can receive on-hit reactions.</param>
/// <param name="Damage">The calculated damage before landing is applied.</param>
public sealed record BattleHitResult(bool Landed, int Damage);
