namespace Dw3.Formats.Battle;

/// <summary>One shared three-dimensional battle effect model and the two archive resources that define it.</summary>
/// <param name="Id">Script-visible effect-model identifier.</param>
/// <param name="ModelFile">Engine file identifier of the model archive.</param>
/// <param name="ModelEntry">Archive entry containing the model descriptor.</param>
/// <param name="MotionFile">Engine file identifier of the motion archive.</param>
/// <param name="MotionEntry">Archive entry containing the effect motion list.</param>
public sealed record EffectModelReference(int Id, int ModelFile, int ModelEntry, int MotionFile, int MotionEntry);
