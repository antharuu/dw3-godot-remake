using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>One actor of a stage with the address of its entry and FieldTalk table.</summary>
/// <param name="Index">Position in the stage actor pointer list.</param>
/// <param name="Address">RAM address of the actor entry.</param>
/// <param name="Entry">Decoded actor entry.</param>
/// <param name="Width">Talk and collision width from the field width table.</param>
/// <param name="Talks">FieldTalk alternatives read from <see cref="NpcActorEntry.TalksPtr"/>.</param>
public sealed record StageActorDialogue(
    int Index,
    uint Address,
    NpcActorEntry Entry,
    int Width,
    IReadOnlyList<FieldTalk> Talks);
