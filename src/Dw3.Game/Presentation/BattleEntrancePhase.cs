namespace Dw3Production.Presentation;

/// <summary>The stages of a source fighter entrance, in playing order.</summary>
internal enum BattleEntrancePhase
{
    /// <summary>The camera fades to the entering side's fighter view while the outgoing model stays.</summary>
    Lead,

    /// <summary>The white flash rises until full white, when the incoming model is put in place.</summary>
    FlashRising,

    /// <summary>The white flash falls back over the incoming model in its entrance motion.</summary>
    FlashFalling,

    /// <summary>The incoming model holds the fighter view before the camera returns.</summary>
    Hold,

    /// <summary>The camera returned to the enemy view and the opponent is visible again.</summary>
    Complete,
}
