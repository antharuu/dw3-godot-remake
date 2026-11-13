namespace Dw3Production.Simulation;

/// <summary>Runs one partner panel's report sequence, including its form rewards and partner reward.</summary>
/// <param name="source">The partner's settled reward data.</param>
internal sealed class FightReportPartnerPanel(FightReportPartner source)
{
    /// <summary>The number of equipped-form slots a panel runs, the base form having its own partner row.</summary>
    private const int SlotCount = 3;

    private readonly bool[] _slotExpApplied = new bool[SlotCount];
    private readonly bool[] _slotLevelBlinks = new bool[SlotCount];
    private readonly int[] _slotBlinkFrames = new int[SlotCount];
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
        FightReportSlotView[] slots = [.. Enumerable.Range(0, SlotCount).Select(BuildSlotView)];
        return new FightReportPartnerView(Source.PartyIndex, Source.PartnerId, Source.FighterId, Fade.Level, Selected, CursorFrame, LevelBlinkFrame, ShownExp, Source.LevelAfter, AnimationFrame, slots);
    }

    /// <summary>Builds a source slot view or a hidden placeholder.</summary>
    /// <param name="index">Source slot index.</param>
    /// <returns>The slot snapshot.</returns>
    private FightReportSlotView BuildSlotView(int index) => index < Source.SlotDigimonIds.Count
        ? new FightReportSlotView(Source.SlotDigimonIds[index], true, _slotExpApplied[index], _slotBlinkFrames[index], SlotLevel(index))
        : new FightReportSlotView(-1, false, false, 0, 0);

    /// <summary>Returns the settled level for an equipped source slot.</summary>
    /// <param name="index">Equipped source slot index.</param>
    /// <returns>The displayed level, or zero when a legacy input did not carry a slot snapshot.</returns>
    private int SlotLevel(int index) => index < Source.SlotLevels.Count ? Source.SlotLevels[index] : 0;

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

    /// <summary>Applies settled display state to a used slot. An unused or empty slot legitimately has no row.</summary>
    /// <param name="logs">Diagnostics for contradictory settled report data.</param>
    /// <returns>The form-level phase.</returns>
    private FightReportPartnerPhase ApplySlotExp(List<string> logs)
    {
        if (SlotIndex == 0)
        {
            LogOrphanRows(logs);
        }

        FightReportFormSlot? slot = CurrentFormSlot();
        if (slot is not null)
        {
            _slotExpApplied[SlotIndex] = slot.ExpGained > 0;
        }

        return FightReportPartnerPhase.SlotLevel;
    }

    /// <summary>Logs settled rows that name no equipped slot, which no valid settlement produces.</summary>
    /// <param name="logs">Report diagnostics.</param>
    private void LogOrphanRows(List<string> logs)
    {
        foreach (FightReportFormSlot row in Source.FormSlots.Where(row => !MatchesSlot(row)))
        {
            logs.Add($"report-form-slot-orphan:{Source.PartyIndex}:{row.Slot}:{row.DigimonId}");
        }
    }

    /// <summary>Starts the level-up message when this used form advanced. Without a level-up the original skips the slot's skills.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The level-message wait or the next slot.</returns>
    private FightReportPartnerPhase StartSlotLevel(FightReportMessageBox messages)
    {
        FightReportFormSlot? slot = CurrentFormSlot();
        if (slot is null || slot.LevelAfter <= slot.LevelBefore)
        {
            return FightReportPartnerPhase.NextSlot;
        }

        _slotLevelBlinks[SlotIndex] = true;
        messages.Start(4);
        return FightReportPartnerPhase.SlotLevelMessage;
    }

    /// <summary>Shows the acquisition message for each skill the leveled form newly acquired.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The acquisition wait, or the mastery phase once every acquisition was shown.</returns>
    private FightReportPartnerPhase PreviewSkill(FightReportMessageBox messages)
    {
        IReadOnlyList<int> acquired = CurrentFormSlot()?.AcquiredTechniques ?? [];
        if (_skillIndex >= acquired.Count)
        {
            _skillIndex = 0;
            return FightReportPartnerPhase.SlotSkillLearn;
        }

        messages.Start(5, acquired[_skillIndex++]);
        return FightReportPartnerPhase.SlotSkillPreviewMessage;
    }

    /// <summary>Shows the mastery message for each skill the leveled form newly mastered.</summary>
    /// <param name="messages">The shared report message box.</param>
    /// <returns>The mastery wait, or the next slot once every mastery was shown.</returns>
    private FightReportPartnerPhase LearnSkill(FightReportMessageBox messages)
    {
        IReadOnlyList<int> mastered = CurrentFormSlot()?.MasteredTechniques ?? [];
        if (_skillIndex >= mastered.Count)
        {
            _skillIndex = 0;
            return FightReportPartnerPhase.NextSlot;
        }

        messages.Start(6, mastered[_skillIndex++]);
        return FightReportPartnerPhase.SlotSkillLearnMessage;
    }

    /// <summary>Moves to the following slot, or the partner-level sequence after the last source slot.</summary>
    /// <returns>The next source phase.</returns>
    private FightReportPartnerPhase NextSlot()
    {
        SlotIndex++;
        return SlotIndex < SlotCount ? FightReportPartnerPhase.SlotExp : FightReportPartnerPhase.PartnerLevel;
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
    /// <returns>The form-message wait, the slot notice, or blink wait.</returns>
    /// <remarks>After at least one unlock, the original shows its notice when four or more forms besides the base
    /// are acquired, whether or not the new form found a free slot.</remarks>
    private FightReportPartnerPhase ShowNewDigimon(FightReportMessageBox messages)
    {
        if (_learnedFormIndex < Source.LearnedForms.Count)
        {
            messages.Start(8, Source.LearnedForms[_learnedFormIndex++].NameId);
            return FightReportPartnerPhase.NewDigimonMessage;
        }

        if (Source.LearnedForms.Count > 0 && Source.AcquiredFormCount >= 4)
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

    /// <summary>Returns the settled row of the current slot.</summary>
    /// <returns>The row for a used slot, or null for an unused or empty slot.</returns>
    private FightReportFormSlot? CurrentFormSlot() => Source.FormSlots.FirstOrDefault(row => row.Slot == SlotIndex && MatchesSlot(row));

    /// <summary>Tests whether a settled row names the form equipped in its slot.</summary>
    /// <param name="row">Settled row.</param>
    /// <returns>True when the row's slot holds the row's form.</returns>
    private bool MatchesSlot(FightReportFormSlot row) =>
        row.Slot >= 0 && row.Slot < Source.SlotDigimonIds.Count && Source.SlotDigimonIds[row.Slot] == row.DigimonId;

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
