namespace Dw3Production.Conversion;

/// <summary>One talk action (code, value) resolved to its action family. Actions are carried
/// verbatim and signalled; the dialogue runtime never applies them.</summary>
public sealed record DialogueAction(int Code, int Value, string Kind);
