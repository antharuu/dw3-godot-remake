namespace Dw3Production.Presentation;

/// <summary>One published battle-model state observed after production Present advanced its PAL clock.</summary>
/// <param name="SourceFrame">Pinned elapsed source PAL frames at this observation point.</param>
/// <param name="Motion">Active one-based source motion.</param>
/// <param name="Keyframe">Expanded-motion cursor after the current publication and advance.</param>
/// <param name="Frame">Published source frame or blend destination.</param>
/// <param name="Blend">Published fixed-point blend weight.</param>
/// <param name="BlendTarget">Published blend target, or zero when not blending.</param>
/// <param name="BlendSource">Published blend origin, or negative one when none is selected.</param>
/// <param name="Blending">Whether the model applied a blended skeleton pose.</param>
/// <param name="Bones">Effective world transforms of every source bone.</param>
public sealed record BattleMotionCadenceSample(
    int SourceFrame,
    int Motion,
    int Keyframe,
    int Frame,
    int Blend,
    int BlendTarget,
    int BlendSource,
    bool Blending,
    IReadOnlyList<BattleMotionCadenceBone> Bones);
