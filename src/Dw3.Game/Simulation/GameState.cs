using System.Numerics;

namespace Dw3Production.Simulation;

public sealed class GameState
{
    public Vector2 Position { get; set; }
    public int Facing { get; set; }
    public string ZoneId { get; set; } = "S200";
    public int Progress { get; set; }
    public bool Offer { get; set; }
    // Resolved source direction of the currently offered trigger. A tolerant offer
    // can be beside rather than under the actor, so presentation must not resample
    // the actor-center trigger cell.
    public int OfferedTriggerDirection { get; set; } = -1;
    public int AnimationSet { get; set; }
    public int AnimationDirection { get; set; }
    public int AnimationTicks { get; set; }
    public bool IsRunning { get; set; }
    // Climb state, written by ClimbController and read by presentation. ClimbHeight
    // is the source climbHeight in 1/256-pixel units (0 at the wall foot); the sprite
    // is drawn climbHeight/256 pixels above its foot while the shadow stays below.
    public bool ClimbActive { get; set; }
    public int ClimbHeight { get; set; }
    public int ClimbSide { get; set; }
    public int ClimbAnimationSet { get; set; }
    public int ClimbAnimationTicks { get; set; }
    // Frames of camera smoothing to hold after a climb hop (GetOverEdge / ClimbOffTop),
    // mirroring FIELDSTG_followWithCamera(0, 2): the camera pans instead of snapping.
    public int CameraSmoothingTicks { get; set; }
    // M6 battle mode: true while the field-to-battle transition/stub runs. Locomotion,
    // warp offers and climb/drop are suspended exactly like dialogue while it is active.
    public bool BattleActive { get; set; }

    public void SetFacing(int facing)
    {
        if (facing is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(facing));
        Facing = facing;
    }

    /// <summary>
    /// Restores normal field locomotion control after a climb or drop sequence ends.
    /// Shared by <c>ClimbController.EndClimb</c> and <c>DropController.EndDrop</c>.
    /// </summary>
    public void RestoreFieldControl()
    {
        ClimbActive = false;
        ClimbHeight = 0;
        AnimationSet = 1;
        AnimationDirection = Facing;
        AnimationTicks = 0;
    }
}
