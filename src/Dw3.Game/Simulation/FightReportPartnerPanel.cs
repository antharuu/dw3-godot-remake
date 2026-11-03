namespace Dw3Production.Simulation;

/// <summary>
/// One party member's report panel: its fade, cursor, level blink and idle animation, the
/// rolling experience counter, and the sequence it runs while raised (three Digimon slots, the
/// level-up message, the wait for the level blink to finish).
/// </summary>
/// <param name="source">The partner's reward data.</param>
internal sealed class FightReportPartnerPanel(FightReportPartner source)
{
    /// <summary>The log entry written once per report when a Digimon slot's experience is skipped.</summary>
    private const string SlotExpUnwiredLog = "report-digimon-exp-unwired";

    /// <summary>Gets the partner's reward data: experience before and gained, levels before and after, and its Digimon slots.</summary>
    public FightReportPartner Source { get; } = source;

    /// <summary>Gets the fade of the whole panel, 10 frames in and out.</summary>
    public FightReportFade Fade { get; } = new();

    /// <summary>Gets or sets the step of the panel's sequence. The report controller sets it to start a fade or the experience sequence.</summary>
    public FightReportPartnerPhase Phase { get; set; }

    /// <summary>Gets or sets a value indicating whether the panel is the one being raised, which animates its cursor.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets the cursor animation frame, 0..3, advanced every 5 frames while selected.</summary>
    public int CursorFrame { get; private set; }

    /// <summary>Gets a value indicating whether the level indicator blinks after a level-up.</summary>
    public bool LevelBlink { get; private set; }

    /// <summary>Gets the level blink frame, 0..10, advanced every 3 frames and held at 10.</summary>
    public int LevelBlinkFrame { get; private set; }

    /// <summary>Gets the experience value drawn on the panel, rolling towards the new total for ten frames.</summary>
    public int ShownExp { get; private set; } = source.ExpBefore;

    /// <summary>Gets the idle animation frame of the partner's sprite, advanced every 13 frames while the panel is visible.</summary>
    public int AnimationFrame { get; private set; }

    /// <summary>Gets the Digimon slot being processed, 0..2, then 3 once all slots are done.</summary>
    public int SlotIndex { get; private set; }

    /// <summary>Gets or sets a value indicating whether the experience counter has started rolling. It is set once the experience message was read.</summary>
    public bool ExpSequenceStarted { get; set; }

    /// <summary>Gets the number of frames the experience counter has rolled. At 10 it shows the exact total.</summary>
    public int RollTime { get; private set; }

    /// <summary>
    /// Advances the panel one frame: the cursor turns every 5 frames, the level blink every 3
    /// (up to frame 10), the idle animation every 13 while visible; the experience counter rolls
    /// once the sequence has started; then the panel's phase runs.
    /// </summary>
    /// <param name="time">The report's frame counter.</param>
    /// <param name="messages">The report's message line, for the level-up message.</param>
    /// <param name="logs">The report's diagnostics, for the skipped slot experience.</param>
    public void Tick(int time, FightReportMessageBox messages, List<string> logs)
    {
        if (Selected && time % 5 == 0)
        {
            CursorFrame = (CursorFrame + 1) % 4;
        }

        if (LevelBlink && time % 3 == 0)
        {
            LevelBlinkFrame = Math.Min(10, LevelBlinkFrame + 1);
        }

        if (Fade.Level != 0 && time % 13 == 0)
        {
            AnimationFrame++;
        }

        if (ExpSequenceStarted && Phase != FightReportPartnerPhase.Hidden)
        {
            RollExp();
        }

        Phase = NextPhase(messages, logs);
    }

    /// <summary>Builds the panel as drawn this frame; slots past the partner's Digimon list are hidden.</summary>
    /// <returns>The panel view.</returns>
    public FightReportPartnerView ToView()
    {
        var slotIds = Source.SlotDigimonIds;
        var slots = Enumerable.Range(0, 3)
            .Select(index => new FightReportSlotView(index < slotIds.Count ? slotIds[index] : -1, index < slotIds.Count, false, 0))
            .ToArray();
        return new FightReportPartnerView(
            Source.PartyIndex,
            Source.PartnerId,
            Source.FighterId,
            Fade.Level,
            Selected,
            CursorFrame,
            LevelBlinkFrame,
            ShownExp,
            AnimationFrame,
            slots);
    }

    /// <summary>Runs the current phase for one frame and returns the phase for the next frame.</summary>
    /// <param name="messages">The report's message line.</param>
    /// <param name="logs">The report's diagnostics.</param>
    /// <returns>The phase for the next frame.</returns>
    private FightReportPartnerPhase NextPhase(FightReportMessageBox messages, List<string> logs) => Phase switch
    {
        FightReportPartnerPhase.ShowStart => StartFade(fadeIn: true),
        FightReportPartnerPhase.ShowFade => Fade.Update() ? FightReportPartnerPhase.Idle : Phase,
        FightReportPartnerPhase.HideStart => StartFade(fadeIn: false),
        FightReportPartnerPhase.HideFade => Fade.Update() ? FightReportPartnerPhase.Hidden : Phase,
        FightReportPartnerPhase.SlotExp => SkipSlotExp(logs),
        FightReportPartnerPhase.SlotLevel => FightReportPartnerPhase.NextSlot,
        FightReportPartnerPhase.NextSlot => NextSlot(),
        FightReportPartnerPhase.PartnerLevel => PartnerLevel(messages),
        FightReportPartnerPhase.LevelMessage => messages.TakeFinished() ? FightReportPartnerPhase.NewDigimon : Phase,
        FightReportPartnerPhase.NewDigimon => FightReportPartnerPhase.WaitBlinks,
        FightReportPartnerPhase.WaitBlinks => WaitBlinks(),
        _ => Phase,
    };

    /// <summary>Starts the panel's 10-frame fade and moves to its waiting phase.</summary>
    /// <param name="fadeIn">Whether the panel fades in.</param>
    /// <returns>The matching fade phase.</returns>
    private FightReportPartnerPhase StartFade(bool fadeIn)
    {
        Fade.Start(fadeIn, 10);
        return fadeIn ? FightReportPartnerPhase.ShowFade : FightReportPartnerPhase.HideFade;
    }

    /// <summary>
    /// The Digimon slot's experience is not modelled yet: the slot is skipped, and the gap is
    /// logged once per report.
    /// </summary>
    /// <param name="logs">The report's diagnostics.</param>
    /// <returns>The slot level phase.</returns>
    private static FightReportPartnerPhase SkipSlotExp(List<string> logs)
    {
        if (!logs.Contains(SlotExpUnwiredLog))
        {
            logs.Add(SlotExpUnwiredLog);
        }

        return FightReportPartnerPhase.SlotLevel;
    }

    /// <summary>Moves to the next of the three Digimon slots, then to the partner's own level.</summary>
    /// <returns>The next slot's experience phase, or the partner level phase after the third slot.</returns>
    private FightReportPartnerPhase NextSlot()
    {
        SlotIndex++;
        return SlotIndex < 3 ? FightReportPartnerPhase.SlotExp : FightReportPartnerPhase.PartnerLevel;
    }

    /// <summary>On a level-up, starts the level blink and message 7 (partner, new level).</summary>
    /// <param name="messages">The report's message line.</param>
    /// <returns>The level message phase on a level-up, otherwise the new Digimon phase.</returns>
    private FightReportPartnerPhase PartnerLevel(FightReportMessageBox messages)
    {
        if (Source.LevelAfter <= Source.LevelBefore)
        {
            return FightReportPartnerPhase.NewDigimon;
        }

        LevelBlink = true;
        messages.Start(7, Source.PartnerId, Source.LevelAfter);
        return FightReportPartnerPhase.LevelMessage;
    }

    /// <summary>Releases the panel once the level blink, if any, has reached its last frame.</summary>
    /// <returns>The idle phase once the blink is over, otherwise the current phase.</returns>
    private FightReportPartnerPhase WaitBlinks()
    {
        if (LevelBlink && LevelBlinkFrame < 10)
        {
            return Phase;
        }

        Selected = false;
        return FightReportPartnerPhase.Idle;
    }

    /// <summary>
    /// Rolls the experience counter towards its target (capped at 999999) like a decimal wheel
    /// for ten frames, then shows the exact target. Wrapping past nine steps keeps every digit cycling 0..9.
    /// </summary>
    private void RollExp()
    {
        var target = Math.Min(999999, Source.ExpBefore + Source.ExpGained);
        if (++RollTime >= 10)
        {
            ShownExp = target;
            return;
        }

        var step = FightReportController.RollStep(target);
        ShownExp += step;
        if (ShownExp > step * 9)
        {
            ShownExp -= (step * 9) + 1;
        }
    }
}
