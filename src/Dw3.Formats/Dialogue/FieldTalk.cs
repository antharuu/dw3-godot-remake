using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// One alternative from a 12-byte FieldTalk table record.
/// </summary>
/// <param name="Conditions">Condition pairs that must all hold before the talk is available.</param>
/// <param name="Actions">Condition pairs applied after this talk is shown.</param>
/// <param name="Entry">Signed 32-bit reference to the selected text-table entry.</param>
public sealed record FieldTalk(IReadOnlyList<NpcCondition> Conditions, IReadOnlyList<NpcCondition> Actions, int Entry);
