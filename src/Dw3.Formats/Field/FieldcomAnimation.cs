namespace Dw3.Formats.Field;

/// <summary>An icon animation whose loop sentinel points to <see cref="LoopTarget"/>.</summary>
public sealed record FieldcomAnimation(IReadOnlyList<FieldcomFrameStep> Steps, int LoopTarget, bool HasTrailingPadding = false);
