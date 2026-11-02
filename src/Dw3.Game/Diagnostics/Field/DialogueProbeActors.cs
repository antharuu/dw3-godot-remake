using Dw3Production.Conversion;
using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Picks the actors the dialogue probe talks to and the place it stands to talk to them. The
/// probe needs a named speaker with at least two pages, so it can check the name window and
/// page advance, and an actor whose talk carries a story effect.
/// </summary>
internal static class DialogueProbeActors
{
    /// <summary>Text control that brackets a speaker name.</summary>
    private const int NameBracketCommand = 7;

    /// <summary>Text control that stands for the player's own name.</summary>
    private const int PlayerNameCommand = 9;

    /// <summary>Source facing 6 (right): its step is (16, 0), so the faced tile is 16 units to the right.</summary>
    private const int FacingRight = 6;

    /// <summary>
    /// Returns the first actor of the zone whose selected talk has a non-player speaker name and
    /// at least two pages, and whose interaction box is found at its own position.
    /// </summary>
    /// <param name="dialogue">Dialogue tables.</param>
    /// <param name="zone">Zone conversations.</param>
    /// <returns>The actor.</returns>
    /// <exception cref="InvalidOperationException">The zone has no such actor.</exception>
    public static DialogueActor FindNamedMultiPageActor(DialogueData dialogue, ZoneDialogue zone) =>
        zone.Actors.FirstOrDefault(actor =>
            HasMultiPageTalk(dialogue, zone, actor)
            && HasNonPlayerSpeakerName(dialogue, zone, actor)
            && DialogueActorLocator.FindAt(zone, new NumericsVector2(actor.X, actor.Y))?.Id == actor.Id)
        ?? throw new InvalidOperationException("S200 has no reachable named multi-page selected talk");

    /// <summary>Returns the first actor whose selected talk is an effect talk.</summary>
    /// <param name="zone">Zone conversations.</param>
    /// <returns>The actor.</returns>
    /// <exception cref="InvalidOperationException">The zone has no such actor.</exception>
    public static DialogueActor FindEffectActor(ZoneDialogue zone) =>
        zone.Actors.FirstOrDefault(actor => actor.Talks.Any(talk => talk.Selected && talk.Kind == "effect"))
        ?? throw new InvalidOperationException("S200 has no effect actor");

    /// <summary>Places the player one source step left of an actor, facing it, so the faced tile is the actor's tile.</summary>
    /// <param name="actor">Actor to face.</param>
    /// <returns>The player position and facing.</returns>
    public static (NumericsVector2 Position, int Facing) FaceActor(DialogueActor actor) =>
        (new NumericsVector2(actor.X - 16, actor.Y), FacingRight);

    /// <summary>Returns the text of an actor's selected talk.</summary>
    /// <param name="dialogue">Dialogue tables.</param>
    /// <param name="zone">Zone conversations, naming the talk file.</param>
    /// <param name="actor">Actor.</param>
    /// <returns>The talk entry, or <see langword="null"/> when nothing is selected or the entry is out of the file.</returns>
    private static IReadOnlyList<TextToken>? SelectedEntry(DialogueData dialogue, ZoneDialogue zone, DialogueActor actor)
    {
        var talk = actor.Talks.FirstOrDefault(candidate => candidate.Selected);
        if (talk is null || !dialogue.Texts.TryGetValue(zone.TalkFile, out var entries))
        {
            return null;
        }

        return talk.Entry >= 0 && talk.Entry < entries.Count ? entries[talk.Entry] : null;
    }

    /// <summary>Tells whether the selected talk splits into at least two pages, so it can advance.</summary>
    /// <param name="dialogue">Dialogue tables.</param>
    /// <param name="zone">Zone conversations.</param>
    /// <param name="actor">Actor.</param>
    /// <returns><see langword="true"/> for a multi-page talk.</returns>
    private static bool HasMultiPageTalk(DialogueData dialogue, ZoneDialogue zone, DialogueActor actor) =>
        SelectedEntry(dialogue, zone, actor) is { } entry && DialogueController.SplitPages(entry).Count >= 2;

    /// <summary>
    /// Tells whether the selected talk starts with a real speaker name: a pair of name bracket
    /// controls around at least one token, other than the lone player-name control.
    /// </summary>
    /// <param name="dialogue">Dialogue tables.</param>
    /// <param name="zone">Zone conversations.</param>
    /// <param name="actor">Actor.</param>
    /// <returns><see langword="true"/> for a named non-player speaker.</returns>
    private static bool HasNonPlayerSpeakerName(DialogueData dialogue, ZoneDialogue zone, DialogueActor actor)
    {
        if (SelectedEntry(dialogue, zone, actor) is not { } entry)
        {
            return false;
        }

        var brackets = entry.Select((token, index) => (token, index))
            .Where(pair => pair.token.IsControl && pair.token.Command == NameBracketCommand)
            .Select(pair => pair.index)
            .Take(2)
            .ToList();
        if (brackets.Count < 2)
        {
            return false;
        }

        var nameCount = brackets[1] - brackets[0] - 1;
        var onlyPlayerName = nameCount == 1 && entry[brackets[0] + 1].IsControl && entry[brackets[0] + 1].Command == PlayerNameCommand;
        return nameCount != 0 && !onlyPlayerName;
    }
}
