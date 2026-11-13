namespace Dw3Production.Simulation;

/// <summary>Runs one partner panel's report sequence, including its form rewards and partner reward.</summary>
/// <param name="source">The partner's settled reward data.</param>
internal sealed class FightReportPartnerPanel(FightReportPartner source)
{
    private readonly int _slotCount = Math.Max(3, source.SlotDigimonIds.Count);
    private readonly bool[] _slotExpApplied = new bool[4];
    private readonly bool[] _slotLevelBlinks = new bool[4];
    private readonly int[] _slotBlinkFrames = new int[4];
    private int _skillIndex;
    private int _learnedFormIndex;

    /// <summary>Gets the partner's reward data.</summary>
    public FightReportPartner Source { get; } = source;

    /// <summary>Gets the fade of the whole panel.</summary>
    public FightReportFade Fade { get; } = new();

    /// <summary>Gets or sets a value indicating whether the active report phase is set.</summary>
    public FightReportPartnerPhase Phase { get; set; }

    /// <summary>Gets or sets a value indicating whether this panel is raised by the controller.</summary>
    public bool Selected { get; set; }

    /// <summary>Gets the selected cursor animation frame.</summary>
    public int CursorFrame { get; private set; }

    /// <summary>Gets a value indicating whether the partner level indicator blinks.</summary>
    public bool LevelBlink { get; private set; }

    /// <summary>Gets the partner level blink frame.</summary>
    public int LevelBlinkFrame { get; private set; }

    /// <summary>Gets the experience value drawn on the partner panel.</summary>
    public int ShownExp { get; private set; } = source.ExpBefore;

    /// <summary>Gets the idle animation frame.</summary>
    public int AnimationFrame { get; private set; }

    /// <summary>Gets the current source slot, then three after all slots.</summary>
    public int SlotIndex { get; private set; }

    /// <summary>Gets or sets a value indicating whether the partner experience wheel has started.</summary>
    public bool ExpSequenceStarted { get; set; }

    /// <summary>Gets the number of partner experience-wheel frames elapsed.</summary>
    public int RollTime { get; private set; }

    /// <summary>Advances visible animations and one form or partner-report state transition.</summary>
    /// <param name="time">The report frame number.</param>
    /// <param name="messages">The shared report message box.</param>
    /// <param name="logs">The report diagnostics.</param>
    public void Tick(int time, FightReportMessageBox messages, List<string> logs)
    {
        TickCursor(time);
        TickBlinks(time);
        TickAnimation(time);
        if (ExpSequenceStarted && Phase != FightReportPartnerPhase.Hidden)
        {
            RollExp();
        }

        Phase = NextPhase(messages, logs);
    }

    /// <summary>Builds the visible panel and maps settled form rows to display slots.</summary>
    /// <returns>The panel view for this frame.</returns>
    public FightReportPartnerView ToView()
    {
        FightReportSlotView[] slots = [.. Enumerable.Range(0, _slotCount).Select(BuildSlotView)];
        return new FightReportPartnerView(Source.PartyIndex, Source.PartnerId, Source.FighterId, Fade.Level, Selected, CursorFrame, LevelBlinkFrame, ShownExp, AnimationFrame, slots);
    }

    /// <summary>Builds a source slot view or a hidden placeholder.</summary>
    /// <param name="index">Source slot index.</param>
    /// <returns>The slot snapshot.</returns>
    private FightReportSlotView BuildSlotView(int index) => index < Source.SlotDigimonIds.Count
        ? new FightReportSlotView(Source.SlotDigimonIds[index], true, _slotExpApplied[index], _slotBlinkFrames[index])
        : new FightReportSlotView(-1, false, false, 0);

    /// <summary>Advances the selected cursor at its source cadence.</summary>
    /// <param name="time">The report frame number.</param>
    private void TickCursor(int time)
    {
        if (Selected && time % 5 == 0)
        {
            CursorFrame = (CursorFrame + 1) % 4;
        }
    }

    /// <summary>Advances all active partner and form-level blink indicators.</summary>
    /// <param name="time">The report frame number.</param>
    private void TickBlinks(int time)
    {
        if (time % 3 != 0)
        {
            return;
        }

        if (LevelBlink)
        {
            LevelBlinkFrame = Math.Min(10, LevelBlinkFrame + 1);
        }

        for (int index = 0; index < _slotLevelBlinks.Length; index++)
        {
            if (_slotLevelBlinks[index])
            {
                _slotBlinkFrames[index] = Math.Min(10, _slotBlinkFrames[index] + 1);
            }
        }
    }

    /// <summary>Advances the sprite idle animation while the panel is visible.</summary>
    /// <param name="time">The report frame number.</param>
    private void TickAnimation(int time)
    {
        if (Fade.Level != 0 && time % 13 == 0)
        {
            AnimationFrame++;
        }
    }

    /// <summary>Runs one report phase and chooses the following phase.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <param name="logs">Diagnostics retained for contradictory settled data.</param>
    /// <returns>The phase for the next report frame.</returns>
    private FightReportPartnerPhase NextPhase(FightReportMessageBox messages, List<string> logs) => Phase switch
    {
        FightReportPartnerPhase.ShowStart => StartFade(true),
        FightReportPartnerPhase.ShowFade => Fade.Update() ? FightReportPartnerPhase.Idle : Phase,
        FightReportPartnerPhase.HideStart => StartFade(false),
        FightReportPartnerPhase.HideFade => Fade.Update() ? FightReportPartnerPhase.Hidden : Phase,
        FightReportPartnerPhase.SlotExp => ApplySlotExp(logs),
        FightReportPartnerPhase.SlotLevel => StartSlotLevel(messages),
        FightReportPartnerPhase.SlotLevelMessage => TakeMessage(messages, FightReportPartnerPhase.SlotSkillPreview),
        FightReportPartnerPhase.SlotSkillPreview => PreviewSkill(messages),
        FightReportPartnerPhase.SlotSkillPreviewMessage => TakeMessage(messages, FightReportPartnerPhase.SlotSkillPreview),
        FightReportPartnerPhase.SlotSkillLearn => LearnSkill(messages),
        FightReportPartnerPhase.SlotSkillLearnMessage => TakeMessage(messages, FightReportPartnerPhase.SlotSkillLearn),
        FightReportPartnerPhase.NextSlot => NextSlot(),
        FightReportPartnerPhase.PartnerLevel => PartnerLevel(messages),
        FightReportPartnerPhase.LevelMessage => TakeMessage(messages, FightReportPartnerPhase.NewDigimon),
        FightReportPartnerPhase.NewDigimon => ShowNewDigimon(messages),
        FightReportPartnerPhase.NewDigimonMessage => TakeMessage(messages, FightReportPartnerPhase.NewDigimon),
        FightReportPartnerPhase.SlotsFullMessage => TakeMessage(messages, FightReportPartnerPhase.WaitBlinks),
        FightReportPartnerPhase.WaitBlinks => WaitBlinks(),
        _ => Phase,
    };

    /// <summary>Starts one panel fade.</summary>
    /// <param name="fadeIn">Whether the panel becomes visible.</param>
    /// <returns>The matching fade phase.</returns>
    private FightReportPartnerPhase StartFade(bool fadeIn)
    {
        Fade.Start(fadeIn, 10);
        return fadeIn ? FightReportPartnerPhase.ShowFade : FightReportPartnerPhase.HideFade;
    }

    /// <summary>Applies settled display state only to a used permanent form.</summary>
    /// <param name="logs">Diagnostics for contradictory settled report data.</param>
    /// <returns>The form-level phase.</returns>
    private FightReportPartnerPhase ApplySlotExp(List<string> logs)
    {
        FightReportFormSlot? slot = CurrentFormSlot();
        if (slot is not null)
        {
            _slotExpApplied[SlotIndex] = slot.ExpGained > 0;
        }
        else if (CurrentSlotId() > 0 && Source.FormSlots.Count > 0)
        {
            logs.Add($"report-form-slot-missing:{Source.PartyIndex}:{SlotIndex}");
        }

        return FightReportPartnerPhase.SlotLevel;
    }

    /// <summary>Starts the source level-up message only when this used form advanced.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The level-message wait or skill-preview phase.</returns>
    private FightReportPartnerPhase StartSlotLevel(FightReportMessageBox messages)
    {
        FightReportFormSlot? slot = CurrentFormSlot();
        if (slot is null || slot.LevelAfter <= slot.LevelBefore)
        {
            return FightReportPartnerPhase.SlotSkillPreview;
        }

        _slotLevelBlinks[SlotIndex] = true;
        messages.Start(4);
        return FightReportPartnerPhase.SlotLevelMessage;
    }

    /// <summary>Shows the source pre-learning message for each newly acquired technique.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The preview wait, committed-learning phase, or next slot.</returns>
    private FightReportPartnerPhase PreviewSkill(FightReportMessageBox messages)
    {
        FightReportFormSlot? slot = CurrentFormSlot();
        if (slot is null || _skillIndex >= slot.LearnedTechniques.Count)
        {
            _skillIndex = 0;
            return slot is null || slot.LearnedTechniques.Count == 0 ? FightReportPartnerPhase.NextSlot : FightReportPartnerPhase.SlotSkillLearn;
        }

        messages.Start(5, slot.LearnedTechniques[_skillIndex++]);
        return FightReportPartnerPhase.SlotSkillPreviewMessage;
    }

    /// <summary>Shows the source committed-learning message for each settled technique delta.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The learning-message wait or next slot.</returns>
    private FightReportPartnerPhase LearnSkill(FightReportMessageBox messages)
    {
        FightReportFormSlot? slot = CurrentFormSlot();
        if (slot is null || _skillIndex >= slot.LearnedTechniques.Count)
        {
            _skillIndex = 0;
            return FightReportPartnerPhase.NextSlot;
        }

        messages.Start(6, slot.LearnedTechniques[_skillIndex++]);
        return FightReportPartnerPhase.SlotSkillLearnMessage;
    }

    /// <summary>Moves to the following slot, or the partner-level sequence after the last source slot.</summary>
    /// <returns>The next source phase.</returns>
    private FightReportPartnerPhase NextSlot()
    {
        SlotIndex++;
        return SlotIndex < _slotCount ? FightReportPartnerPhase.SlotExp : FightReportPartnerPhase.PartnerLevel;
    }

    /// <summary>Starts the existing partner level message when the partner advanced.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The partner level-message wait or new-form phase.</returns>
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

    /// <summary>Shows each settled unlocked form with its decoded name entry.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The form-message wait, full-slot notice, or blink wait.</returns>
    private FightReportPartnerPhase ShowNewDigimon(FightReportMessageBox messages)
    {
        if (_learnedFormIndex < Source.LearnedForms.Count)
        {
            messages.Start(8, Source.LearnedForms[_learnedFormIndex++].NameId);
            return FightReportPartnerPhase.NewDigimonMessage;
        }

        if (Source.LearnedForms.Any(form => !Source.SlotDigimonIds.Contains(form.DigimonId)))
        {
            messages.Start(9);
            return FightReportPartnerPhase.SlotsFullMessage;
        }

        return FightReportPartnerPhase.WaitBlinks;
    }

    /// <summary>Consumes a completed message before its continuation phase.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <param name="next">Phase after the player reads the message.</param>
    /// <returns>The continuation phase or current wait phase.</returns>
    private FightReportPartnerPhase TakeMessage(FightReportMessageBox messages, FightReportPartnerPhase next) =>
        messages.TakeFinished() ? next : Phase;

    /// <summary>Waits for every active level blink before making the panel idle again.</summary>
    /// <returns>The idle phase when all blinks reached their terminal frame.</returns>
    private FightReportPartnerPhase WaitBlinks()
    {
        bool slotBlinking = _slotLevelBlinks.Where((blinking, index) => blinking && _slotBlinkFrames[index] < 10).Any();
        if ((LevelBlink && LevelBlinkFrame < 10) || slotBlinking)
        {
            return Phase;
        }

        Selected = false;
        return FightReportPartnerPhase.Idle;
    }

    /// <summary>Returns the settled form row for the current source slot.</summary>
    /// <returns>The row for the used form, or null for an unused or empty slot.</returns>
    private FightReportFormSlot? CurrentFormSlot()
    {
        int id = CurrentSlotId();
        return Source.FormSlots.FirstOrDefault(slot => slot.DigimonId == id);
    }

    /// <summary>Returns the current equipped source slot id, or zero outside the slot list.</summary>
    /// <returns>The slot form id.</returns>
    private int CurrentSlotId() => SlotIndex < Source.SlotDigimonIds.Count ? Source.SlotDigimonIds[SlotIndex] : 0;

    /// <summary>Rolls the partner experience display towards its settled target.</summary>
    private void RollExp()
    {
        int target = Math.Min(999999, Source.ExpBefore + Source.ExpGained);
        if (++RollTime >= 10)
        {
            ShownExp = target;
            return;
        }

        int step = FightReportController.RollStep(target);
        ShownExp += step;
        if (ShownExp > step * 9)
        {
            ShownExp -= (step * 9) + 1;
        }
    }
}
