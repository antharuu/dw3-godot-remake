using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Field dialogue acceptance on S200. Facing a named multi-page speaker, Cross opens the
/// conversation; the page is revealed and the talk box checked with real framebuffer pixels;
/// pages advance one by one until the box closes; locomotion resumes; Cross held through the
/// close rearms instead of reopening; and an effect talk signals that its effect is not applied
/// without changing story progress. The probe places the player directly in front of each actor.
/// </summary>
internal sealed class DialogueProbe : FieldProbe
{
    /// <summary>Upper bound of page advances of the effect conversation.</summary>
    private const int MaxEffectAdvances = 64;

    /// <summary>Upper bound of steps for locomotion to move the player after the conversation.</summary>
    private const int MaxResumeSteps = 12;

    private readonly IDialogueProbeHost _dialogueHost;

    /// <summary>Initializes a new instance of the <see cref="DialogueProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public DialogueProbe(IDialogueProbeHost host)
        : base(host)
    {
        _dialogueHost = host;
    }

    /// <inheritdoc/>
    protected override string FailureCode => "DIALOGUE_PROBE";

    /// <summary>Gets the conversation state machine.</summary>
    private DialogueController Dialogue => _dialogueHost.Dialogue;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var dialogue = _dialogueHost.DialogueData ?? throw new InvalidOperationException("dialogue data unavailable");
        if (!dialogue.Zones.TryGetValue("S200", out var zone))
        {
            throw new InvalidOperationException("S200 has no dialogue zone");
        }

        var simpleActor = DialogueProbeActors.FindNamedMultiPageActor(dialogue, zone);
        var effectActor = DialogueProbeActors.FindEffectActor(zone);
        var captureRoot = Host.PrepareCaptureDirectory();
        await OpenAndCheckBubbleAsync(simpleActor, captureRoot);
        var pageCount = AdvanceToClose(simpleActor);
        ResumeLocomotion(simpleActor);
        CheckHeldCrossRearms(simpleActor);
        CheckEffectNotApplied(effectActor);
        Require(!Host.Input.F3On, "dialogue probe enabled F3");
        QuietLog.Info($"dialogue probe PASS zone=S200 simple_actor={simpleActor.Id} effect_actor={effectActor.Id} pages={pageCount} capture={captureRoot}/dialogue-open.png");
    }

    /// <summary>
    /// Captures the scene in front of the actor, opens the conversation with Cross, reveals the
    /// first page and checks the drawn talk box against the capture without it.
    /// </summary>
    /// <param name="actor">Named multi-page speaker.</param>
    /// <param name="captureRoot">Capture directory.</param>
    /// <returns>A task that completes once the talk box passed its checks.</returns>
    private async Task OpenAndCheckBubbleAsync(DialogueActor actor, string captureRoot)
    {
        PlaceFacing(actor);
        Host.Renderer.Present(Host.State, Host.CurrentZone, f3On: false);
        _dialogueHost.DialogueRenderer.Present(null);
        await Host.NextFrameDrawn();
        var before = Host.CaptureViewport();
        Require(before.SavePng(Path.Combine(captureRoot, "dialogue-before.png")) == Error.Ok, "dialogue before capture failed");

        Host.Input.State.PressAction();
        Host.TickTestSimulation(); // Cross edge -> conversation start
        Require(Dialogue.IsActive, "Cross did not start the simple conversation");
        Host.Input.State.ReleaseAction();
        Host.TickTestSimulation(); // Opening -> Open, the typewriter starts at 0
        Require(Dialogue.State == DialogueState.Open, $"dialogue did not reach Open state={Dialogue.State}");
        Require(Dialogue.PageCount >= 2, $"simple talk has too few pages to advance pages={Dialogue.PageCount}");
        Require(Dialogue.SpeakerName.Count > 0 && !Dialogue.SpeakerIsPlayer, "named talk lost its speaker name");
        TapCross(); // reveal the whole page so every part of the box can be checked
        Require(Dialogue.RevealComplete, "Cross did not reveal the page (showPage)");

        var renderer = _dialogueHost.DialogueRenderer;
        renderer.SetProbeArrowBrightness(0);
        Host.Renderer.Present(Host.State, Host.CurrentZone, f3On: false);
        renderer.Present(_dialogueHost.BuildDialogueView());
        await Host.NextFrameDrawn();
        var openCapture = Host.CaptureViewport();
        Require(openCapture.SavePng(Path.Combine(captureRoot, "dialogue-open.png")) == Error.Ok, "dialogue open capture failed");
        Require(renderer.HasFramePixels, "bubble laid out no frame sprites");
        Require(renderer.HasNamePixels, "bubble laid out no name glyphs");
        Require(renderer.HasArrowPixels, "bubble laid out no arrow");
        Require(renderer.HasGlyphPixels, "bubble laid out no text glyphs");
        new DialogueBubbleAssertions(_dialogueHost).Check(before, openCapture, actor);
        QuietLog.Info($"dialogue render PASS zone=S200 actor={actor.Id} type={renderer.BoxType} glyphs={renderer.GlyphCount} icons={renderer.IconCount} name={renderer.NameGlyphCount} rect={renderer.BoxScreenRect}");
    }

    /// <summary>Advances the open conversation page by page until it closes to Idle.</summary>
    /// <param name="actor">Speaker, for the log.</param>
    /// <returns>The number of pages of the conversation.</returns>
    private int AdvanceToClose(DialogueActor actor)
    {
        var pages = 0;
        var pageCount = Dialogue.PageCount;
        while (Dialogue.IsActive)
        {
            pages += AdvanceOnePage() ? 1 : 0;
        }

        Require(!Dialogue.IsActive && Dialogue.State == DialogueState.Idle, "dialogue did not close to Idle after the last page");
        Require(pages == pageCount - 1, $"dialogue advanced {pages} pages instead of {pageCount - 1}");
        QuietLog.Info($"dialogue advance PASS zone=S200 actor={actor.Id} pages={pageCount} advances={pages}");
        return pageCount;
    }

    /// <summary>
    /// Moves the player back to the S200 spawn and holds right, proving that the closed
    /// conversation released locomotion; the step right of the spawn is walkable.
    /// </summary>
    /// <param name="actor">Speaker, for the log.</param>
    private void ResumeLocomotion(DialogueActor actor)
    {
        Host.State.Position = new NumericsVector2(1000, 236);
        Host.State.SetFacing(1);
        Host.ApplyScene(Host.GetOrLoadZone("S200"));
        var resumeStart = Host.State.Position;
        Host.SetTestHeldDirection(SemanticDirection.Right);
        var moved = false;
        for (var tick = 0; tick < MaxResumeSteps && !moved; tick++)
        {
            Host.SimulateStep(_ => { });
            moved = Host.State.Position != resumeStart;
        }

        Host.SetTestHeldDirection(SemanticDirection.None);
        Require(moved, "locomotion did not resume after the conversation");
        QuietLog.Info($"dialogue resume PASS zone=S200 actor={actor.Id} moved={moved}");
    }

    /// <summary>
    /// Reopens the conversation, reaches its revealed last page, then holds Cross through the
    /// close: the machine must stay in Rearm while Cross is held and reach Idle on release.
    /// </summary>
    /// <param name="actor">Named multi-page speaker.</param>
    private void CheckHeldCrossRearms(DialogueActor actor)
    {
        PlaceFacing(actor);
        Host.Input.State.PressAction();
        Host.TickTestSimulation();
        Host.Input.State.ReleaseAction();
        Host.TickTestSimulation(); // Opening -> Open
        for (var page = 0; page < Dialogue.PageCount - 1; page++)
        {
            AdvanceOnePage();
        }

        if (!Dialogue.RevealComplete)
        {
            TapCross();
        }

        Require(
            Dialogue.State == DialogueState.Open && Dialogue.RevealComplete && Dialogue.PageIndex == Dialogue.PageCount - 1,
            "anti-reopen setup did not reach the revealed last page");
        Host.Input.State.PressAction(); // hold Cross through the close
        Host.TickTestSimulation(); // Open -> Closing
        Host.TickTestSimulation(); // Closing -> Rearm
        Host.TickTestSimulation(); // Rearm while Cross is held: no reopen
        Require(Dialogue.State == DialogueState.Rearm, "held Cross reopened the dialogue instead of rearming");
        Host.Input.State.ReleaseAction();
        Host.TickTestSimulation(); // Rearm -> Idle
        Require(Dialogue.State == DialogueState.Idle, "dialogue did not rearm after the action release");
        QuietLog.Info($"dialogue rearm PASS zone=S200 actor={actor.Id} held-cross-rearmed=true");
    }

    /// <summary>
    /// Runs an effect talk to its end: closing it must signal once that the effect is not applied
    /// and leave story progress unchanged.
    /// </summary>
    /// <param name="actor">Actor with a selected effect talk.</param>
    private void CheckEffectNotApplied(DialogueActor actor)
    {
        PlaceFacing(actor);
        Host.Input.State.PressAction();
        Host.TickTestSimulation();
        Require(Dialogue.IsActive, "Cross did not start the effect conversation");
        Host.Input.State.ReleaseAction();
        Host.TickTestSimulation(); // Opening -> Open
        var progressBefore = Host.State.Progress;
        for (var guard = 0; Dialogue.IsActive && guard < MaxEffectAdvances; guard++)
        {
            AdvanceOnePage();
        }

        Require(!Dialogue.IsActive, "effect conversation did not close");
        Require(
            Host.Log.Warned($"dialogue-effect-not-applied:{Host.CurrentZone.Zone.Id}:{actor.Id}"),
            "effect conversation did not signal dialogue-effect-not-applied");
        Require(Host.State.Progress == progressBefore, "effect conversation changed story progress");
        var talk = Dialogue.Talk ?? throw new InvalidOperationException("effect conversation did not retain its talk data");
        QuietLog.Info($"dialogue effect PASS zone=S200 actor={actor.Id} families=[{string.Join(",", talk.Families)}] progress_unchanged=true");
    }

    /// <summary>Places the player in front of an actor, facing it, with Cross released.</summary>
    /// <param name="actor">Actor to face.</param>
    private void PlaceFacing(DialogueActor actor)
    {
        var (position, facing) = DialogueProbeActors.FaceActor(actor);
        Host.State.Position = position;
        Host.State.SetFacing(facing);
        Host.Input.State.ReleaseAction();
    }

    /// <summary>Presses and releases Cross, one simulation step each.</summary>
    private void TapCross()
    {
        Host.Input.State.PressAction();
        Host.TickTestSimulation();
        Host.Input.State.ReleaseAction();
        Host.TickTestSimulation();
    }

    /// <summary>Reveals the current page if needed, then presses Cross to go to the next page or close the last one.</summary>
    /// <returns><see langword="true"/> when Cross moved to a next page rather than closing.</returns>
    private bool AdvanceOnePage()
    {
        if (!Dialogue.RevealComplete)
        {
            TapCross();
        }

        Host.Input.State.PressAction();
        Host.TickTestSimulation(); // Open -> Advance or Closing
        var advanced = Dialogue.State == DialogueState.Advance;
        Host.Input.State.ReleaseAction();
        Host.TickTestSimulation(); // Advance -> Open, or Closing -> Rearm
        if (Dialogue.State == DialogueState.Rearm)
        {
            Host.TickTestSimulation(); // Rearm -> Idle
        }

        return advanced;
    }
}
