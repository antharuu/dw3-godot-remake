using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>One action set resolved from the archive's five directional references.</summary>
public sealed record ActionSet(int SetId, string Name, ActionSequenceTerminator Terminator, IReadOnlyList<ActionAnimationRecord> Records);
