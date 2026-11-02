namespace Dw3Production.Conversion;

/// <summary>One talk condition (code, value) resolved to its condition family.</summary>
public sealed record DialogueCondition(int Code, int Value, string Kind);
