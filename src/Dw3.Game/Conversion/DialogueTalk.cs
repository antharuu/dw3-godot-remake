namespace Dw3Production.Conversion;

/// <summary>One actor talk: its text entry, conditions, actions, the derived effect kind
/// (simple | effect) and whether the snapshot progress selected it.</summary>
public sealed record DialogueTalk(int Entry, IReadOnlyList<DialogueCondition> Conditions, IReadOnlyList<DialogueAction> Actions, string Kind, IReadOnlyList<string> Families, bool Selected);
