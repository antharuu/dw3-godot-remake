namespace Dw3Production.Presentation;

/// <summary>
/// The physical scene operations used by <see cref="BattleEntranceSequence"/>. The sequence decides when they happen,
/// the stage decides how the renderer performs them, so the timeline is testable without Godot.
/// </summary>
internal interface IBattleEntranceStage
{
    /// <summary>Moves the battle camera to one of the entering side's decoded fighter views.</summary>
    /// <param name="enemy">Whether the view belongs to the enemy side's current model.</param>
    /// <param name="viewIndex">Decoded fighter camera index.</param>
    /// <param name="frames">Source fade frames, zero for an immediate cut.</param>
    void MoveCameraToFighter(bool enemy, int viewIndex, int frames);

    /// <summary>Moves the battle camera to the source enemy view.</summary>
    /// <param name="frames">Source fade frames.</param>
    void MoveCameraToEnemyView(int frames);

    /// <summary>Arms the decoded entrance cue with its prepared lead-in.</summary>
    void BeginEntranceCue();

    /// <summary>Shows or hides the model of the side that is not entering.</summary>
    /// <param name="visible">Whether the opponent model is drawn.</param>
    void SetOpponentVisible(bool visible);

    /// <summary>Replaces the entering side's model under full white and starts its entrance motion.</summary>
    /// <param name="motion">Source motion number of the incoming model.</param>
    void SwapInModel(int motion);

    /// <summary>Gives the incoming model its normal or weak idle.</summary>
    /// <param name="idleMotion">Zero for the normal idle, one for the weak idle.</param>
    void SetEnteringIdle(int idleMotion);

    /// <summary>Draws the additive full-screen white level of the entrance flash.</summary>
    /// <param name="level">Level from 0 to 255.</param>
    void PresentFlash(int level);
}
