namespace Dw3Production.Conversion;

/// <summary>A zone's dialogue: the index of its FSTALK text file and its actors.</summary>
public sealed record ZoneDialogue(int TalkFile, IReadOnlyList<DialogueActor> Actors);
