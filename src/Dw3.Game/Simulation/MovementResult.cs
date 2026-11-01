using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// The outcome of one <see cref="Locomotion.Tick"/> call. Moved is true when the step was
/// accepted (full or slide); Step is the accepted displacement; Rejection is set when the
/// step was rejected with a reason string; Cell/Row/Mirror/SourceDirection carry the floor
/// metadata at the accepted tile for presentation use.
/// </summary>
public readonly record struct MovementResult(bool Moved, Vector2 Step, string? Rejection, int Cell, int Row, bool Mirror, int SourceDirection)
{
    /// <summary>Gets a stopped result (no input, no movement).</summary>
    public static MovementResult Stopped => new(false, Vector2.Zero, null, 0, 0, false, 0);

    /// <summary>
    /// Creates a rejected result with the given reason.
    /// </summary>
    /// <param name="reason">Why the step was blocked (e.g. "void-footprint", "map-bound").</param>
    /// <returns>A MovementResult with Moved=false and the given reason string.</returns>
    public static MovementResult Rejected(string reason) => new(false, Vector2.Zero, reason, 0, 0, false, 0);

    /// <summary>
    /// Creates an accepted result with the given step and floor metadata.
    /// </summary>
    /// <param name="step">The accepted world-space displacement vector.</param>
    /// <param name="cell">Raw floor cell byte at the tile the step began on.</param>
    /// <param name="row">Slope row index (0..6) derived from the cell's low nibble.</param>
    /// <param name="mirror">True when the cell's mirror flag (bit 4) is set.</param>
    /// <param name="sourceDirection">Source direction (0=down CCW) used for the direction-vector lookup.</param>
    /// <returns>A MovementResult with Moved=true and the given displacement and floor metadata.</returns>
    public static MovementResult Accepted(Vector2 step, int cell, int row, bool mirror, int sourceDirection) =>
        new(true, step, null, cell, row, mirror, sourceDirection);
}
