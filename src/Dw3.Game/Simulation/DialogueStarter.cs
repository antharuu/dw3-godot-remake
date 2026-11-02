using Dw3Production.Conversion;
using Dw3Production.Presentation;

namespace Dw3Production.Simulation;

/// <summary>Selects and starts the conversation attached to the actor facing the player.</summary>
public static class DialogueStarter
{
    /// <summary>Starts a selected actor talk when every decoded record is available.</summary>
    /// <param name="request">Active field and dialogue dependencies.</param>
    /// <param name="boxWidth">Receives the measured source-font bubble width.</param>
    /// <returns>True when a conversation started.</returns>
    public static bool TryStart(DialogueStartRequest request, out int boxWidth)
    {
        boxWidth = 0;
        if (!request.Data.Zones.TryGetValue(request.Zone.Zone.Id, out ZoneDialogue? zoneDialogue))
        {
            return false;
        }

        DialogueActor? actor = DialogueActorLocator.FindAt(zoneDialogue, DialogueActorLocator.FacingTile(request.Player));
        DialogueTalk? talk = actor?.Talks.FirstOrDefault(candidate => candidate.Selected);
        if (actor is null || talk is null || !TryGetEntry(request, zoneDialogue, actor, talk, out IReadOnlyList<TextToken> entry))
        {
            return false;
        }

        WarnUnsupportedControls(request, actor, entry);
        request.Controller.Start(request.Zone.Zone.Id, actor, talk, entry);
        boxWidth = DialogueBubbleLayout.MeasureWidth(request.Controller.FullText, request.Data.Font);
        WarnUnrenderedPlayerName(request, actor);
        request.Player.AnimationSet = 1;
        request.Player.AnimationDirection = request.Player.Facing;
        return true;
    }

    /// <summary>Resolves a talk entry after validating its file-relative index.</summary>
    /// <param name="request">Active field context.</param>
    /// <param name="zone">Zone dialogue metadata.</param>
    /// <param name="actor">Speaking actor.</param>
    /// <param name="talk">Selected actor talk.</param>
    /// <param name="entry">Receives the token stream.</param>
    /// <returns>True when the entry exists.</returns>
    private static bool TryGetEntry(
        DialogueStartRequest request,
        ZoneDialogue zone,
        DialogueActor actor,
        DialogueTalk talk,
        out IReadOnlyList<TextToken> entry)
    {
        entry = [];
        if (!request.Data.Texts.TryGetValue(zone.TalkFile, out IReadOnlyList<IReadOnlyList<TextToken>>? entries))
        {
            return false;
        }

        if (talk.Entry < 0 || talk.Entry >= entries.Count)
        {
            request.Warn(
                $"dialogue-entry-out-of-bounds:{request.Zone.Zone.Id}:{actor.Id}",
                $"dialogue entry out of bounds zone={request.Zone.Zone.Id} actor={actor.Id} entry={talk.Entry}");
            return false;
        }

        entry = entries[talk.Entry];
        return true;
    }

    /// <summary>Reports controls whose effects are deliberately outside the current dialogue scope.</summary>
    /// <param name="request">Active field context.</param>
    /// <param name="actor">Speaking actor.</param>
    /// <param name="entry">Selected token stream.</param>
    private static void WarnUnsupportedControls(
        DialogueStartRequest request,
        DialogueActor actor,
        IReadOnlyList<TextToken> entry)
    {
        foreach (int command in entry.Where(DialogueController.IsUnsupported).Select(token => token.Command).Distinct())
        {
            request.Warn(
                $"dialogue-control-unsupported:{command}",
                $"dialogue-control-unsupported cmd={command} zone={request.Zone.Zone.Id} actor={actor.Id}");
        }
    }

    /// <summary>Reports the known missing player-name substitution once per actor.</summary>
    /// <param name="request">Active field context.</param>
    /// <param name="actor">Speaking actor.</param>
    private static void WarnUnrenderedPlayerName(DialogueStartRequest request, DialogueActor actor)
    {
        if (request.Controller.SpeakerIsPlayer)
        {
            request.Warn(
                $"dialogue-player-name-unrendered:{request.Zone.Zone.Id}:{actor.Id}",
                $"dialogue-player-name-unrendered zone={request.Zone.Zone.Id} actor={actor.Id} (name entry not decoded, M4 scope)");
        }
    }
}
