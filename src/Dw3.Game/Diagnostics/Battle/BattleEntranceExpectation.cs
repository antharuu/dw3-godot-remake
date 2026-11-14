using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>What one entrance proof expects from the production battle.</summary>
/// <param name="Label">Capture and log label.</param>
/// <param name="Kind">Expected entrance kind.</param>
/// <param name="OutgoingId">Fighter rendered on the entering side before the entrance.</param>
/// <param name="IncomingId">Fighter rendered on the entering side after the swap.</param>
/// <param name="Idle">Expected idle selection of the incoming model.</param>
/// <param name="ConfirmAfter">Time units the post message stays open before the scripted Cross.</param>
internal sealed record BattleEntranceExpectation(string Label, BattleEntranceKind Kind, int OutgoingId, int IncomingId, int Idle, int ConfirmAfter)
{
    /// <summary>Gets a value indicating whether the enemy side enters.</summary>
    public bool Enemy => Kind is BattleEntranceKind.EnemySwitch or BattleEntranceKind.EnemyReplacement;

    /// <summary>Gets the message key expected before the entrance, or <see langword="null"/>.</summary>
    public string? BeforeKey => Kind switch
    {
        BattleEntranceKind.PlayerSwitch => "message=switch",
        BattleEntranceKind.EnemySwitch => "message=enemySwitch",
        _ => null,
    };

    /// <summary>Gets the message key expected after the entrance.</summary>
    public string AfterKey => Kind switch
    {
        BattleEntranceKind.PlayerBlastReturn => "message=blastReturn",
        BattleEntranceKind.PlayerDedigivolve => "message=dedigivolve",
        _ => Enemy ? "message=enemyEntered" : "message=switchIn",
    };

    /// <summary>Gets the expected source frames of the entrance itself.</summary>
    public int SequenceFrames => 189 + BattleEntranceExtras.For(IncomingId).HoldFrames;

    /// <summary>Gets a value indicating whether the caller opens the post message at the swap (the enemy turn's
    /// voluntary switch) instead of after the entrance.</summary>
    public bool AfterAtSwap => Kind == BattleEntranceKind.EnemySwitch;

    /// <summary>Gets the expected camera return fade of the entering side, in time units.</summary>
    public int ReturnFadeFrames => Enemy ? 1 : BattleEntranceSequence.LeadFrames;
}
