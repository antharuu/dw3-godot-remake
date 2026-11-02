namespace Dw3Production.Conversion;

/// <summary>A dialogue actor at its tile position, direction and half-width box.</summary>
public sealed record DialogueActor(int Id, int X, int Y, int Dir, int? Width, IReadOnlyList<DialogueTalk> Talks);
