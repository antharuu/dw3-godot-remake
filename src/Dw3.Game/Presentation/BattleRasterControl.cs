namespace Dw3Production.Presentation;

/// <summary>Probe-only interventions on the production stream, absent from ordinary rendering.</summary>
internal sealed class BattleRasterControl
{
    /// <summary>Gets or sets the exclusive source-primitive cutoff used for paired destination captures.</summary>
    public int Limit { get; set; } = int.MaxValue;

    /// <summary>Gets or sets the fighter whose complete contribution is removed.</summary>
    public int HiddenFighter { get; set; } = -1;

    /// <summary>Gets or sets a bone to remove independently of the rest of its fighter.</summary>
    public int HiddenBone { get; set; } = -1;

    /// <summary>Gets or sets a value indicating whether ABR deliberately uses opaque coverage.</summary>
    public bool WrongBlend { get; set; }

    /// <summary>Gets or sets a value indicating whether explicit source triangle indices are deliberately removed.</summary>
    public bool ClearIndices { get; set; }

    /// <summary>Gets or sets synthetic primitives for the executor-only source-quad regression.</summary>
    public IReadOnlyList<BattleRasterPrimitive>? Fixture { get; set; }

    /// <summary>Gets or sets the unmodified ordered primitives from the latest production collector.</summary>
    public IReadOnlyList<BattleRasterPrimitive> Ordered { get; set; } = [];

    /// <summary>Checks whether this is the final primitive in the selected diagnostic prefix.</summary>
    /// <param name="primitive">Primitive being emitted through the real executor.</param>
    /// <returns>Whether only this primitive should intentionally use the wrong blend.</returns>
    public bool UsesWrongBlend(BattleRasterPrimitive primitive) =>
        WrongBlend && Limit > 0 && Limit <= Ordered.Count && primitive == Ordered[Limit - 1];

    /// <summary>Tests visibility without changing source order or key calculation.</summary>
    /// <param name="primitive">One collected source primitive.</param>
    /// <returns>Whether the diagnostic intervention permits emission.</returns>
    public bool Includes(BattleRasterPrimitive primitive) =>
        primitive.Fighter != HiddenFighter && primitive.Polygon.Bone != HiddenBone;
}
