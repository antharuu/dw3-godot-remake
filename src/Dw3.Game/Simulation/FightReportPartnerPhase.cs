namespace Dw3Production.Simulation;

/// <summary>One partner panel's report sequence.</summary>
internal enum FightReportPartnerPhase
{
    /// <summary>Shown and waiting.</summary>
    Idle,

    /// <summary>Starts the ten-frame fade in.</summary>
    ShowStart,

    /// <summary>Fades in, then becomes idle.</summary>
    ShowFade,

    /// <summary>Starts the fade out.</summary>
    HideStart,

    /// <summary>Fades out, then becomes hidden.</summary>
    HideFade,

    /// <summary>Fully transparent and no longer rolls experience.</summary>
    Hidden,

    /// <summary>Applies one used form's settled experience display.</summary>
    SlotExp,

    /// <summary>Checks the current used form's level delta.</summary>
    SlotLevel,

    /// <summary>Waits for the form level-up message.</summary>
    SlotLevelMessage,

    /// <summary>Starts a form technique preview message.</summary>
    SlotSkillPreview,

    /// <summary>Waits for a form technique preview message.</summary>
    SlotSkillPreviewMessage,

    /// <summary>Starts a form technique learned message.</summary>
    SlotSkillLearn,

    /// <summary>Waits for a form technique learned message.</summary>
    SlotSkillLearnMessage,

    /// <summary>Moves to the next source slot or partner level.</summary>
    NextSlot,

    /// <summary>Checks for a partner level-up.</summary>
    PartnerLevel,

    /// <summary>Waits for the partner level-up message.</summary>
    LevelMessage,

    /// <summary>Starts a newly acquired form message.</summary>
    NewDigimon,

    /// <summary>Waits for a newly acquired form message.</summary>
    NewDigimonMessage,

    /// <summary>Waits for the full form-slot notice.</summary>
    SlotsFullMessage,

    /// <summary>Waits for partner and form level blinks.</summary>
    WaitBlinks,
}
