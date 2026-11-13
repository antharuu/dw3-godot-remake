using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>A message the source caller of an entrance shows around it, with its own box lifetime.</summary>
/// <param name="Text">Decoded message lines.</param>
/// <param name="Lines">Number of lines the box reveals, one or two.</param>
internal sealed record BattleEntranceMessage(BattleTextLine Text, int Lines);
