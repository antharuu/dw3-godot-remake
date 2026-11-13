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

    /// <summary>Starts the next skill-acquisition message of a leveled form.</summary>
    SlotSkillPreview,

    /// <summary>Waits for a skill-acquisition message.</summary>
    SlotSkillPreviewMessage,

    /// <summary>Starts the next skill-mastery message of a leveled form.</summary>
    SlotSkillLearn,

    /// <summary>Waits for a skill-mastery message.</summary>
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

    /// <summary>Waits for the notice shown after an unlock once four or more forms are acquired.</summary>
    SlotsFullMessage,

    /// <summary>Waits for partner and form level blinks.</summary>
    WaitBlinks,
}
