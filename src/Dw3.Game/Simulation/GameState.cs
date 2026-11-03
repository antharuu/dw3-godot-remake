using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// The player's field state shared by the simulation controllers and read by the renderer:
/// position, facing, current zone, story progress, animation, and the climb, camera and modal
/// fields. Engine-free: positions are plain vectors in map pixels.
/// </summary>
public sealed class GameState
{
    /// <summary>Gets or sets the actor's foot position in map pixels.</summary>
    public Vector2 Position { get; set; }

    /// <summary>Gets or sets the facing direction in disc numbering, 0..7 with 0 facing down (<see cref="SemanticDirection"/> + 4, modulo 8). Use <see cref="SetFacing"/> to have it range-checked.</summary>
    public int Facing { get; set; }

    /// <summary>Gets or sets the current zone name, <c>Snnn</c>. A new game starts in S200.</summary>
    public string ZoneId { get; set; } = "S200";

    /// <summary>Gets or sets the story progress value tested by story-gated exits and talks.</summary>
    public int Progress { get; set; }

    /// <summary>Gets or sets a value indicating whether a warp is being offered to the player, which shows the exit cue.</summary>
    public bool Offer { get; set; }

    /// <summary>Gets or sets the resolved direction of the currently offered trigger, -1 when none.
    /// A tolerant offer can be beside rather than under the actor, so presentation must not resample
    /// the actor-center trigger cell.</summary>
    public int OfferedTriggerDirection { get; set; } = -1;

    /// <summary>Gets or sets the player animation set: 1 standing, 4 walking, 5 running.</summary>
    public int AnimationSet { get; set; }

    /// <summary>Gets or sets the direction whose sequence of the animation set is played, 0..7.</summary>
    public int AnimationDirection { get; set; }

    /// <summary>Gets or sets the ticks elapsed in the current animation, reset when the set changes.</summary>
    public int AnimationTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether the player runs (the default) rather than walks with the walk modifier.</summary>
    public bool IsRunning { get; set; }

    /// <summary>Gets or sets a value indicating whether a climb or drop drives the actor. Written by the climb and drop controllers.</summary>
    public bool ClimbActive { get; set; }

    /// <summary>Gets or sets the climb height in 1/256-pixel units (0 at the wall foot); the sprite
    /// is drawn ClimbHeight/256 pixels above its foot while the shadow stays below.</summary>
    public int ClimbHeight { get; set; }

    /// <summary>Gets or sets the side of the wall the actor climbs on: 1 right, 0 left.</summary>
    public int ClimbSide { get; set; }

    /// <summary>Gets or sets the climb or drop animation set being played, 0x16..0x20 in the action archive.</summary>
    public int ClimbAnimationSet { get; set; }

    /// <summary>Gets or sets the ticks elapsed in the climb or drop animation.</summary>
    public int ClimbAnimationTicks { get; set; }

    /// <summary>Gets or sets the frames of camera smoothing to hold after a climb hop (over the edge or off the top),
    /// as the original does, so the camera pans instead of snapping.</summary>
    public int CameraSmoothingTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether a battle runs, from the field-to-battle transition on. Locomotion,
    /// warp offers and climb/drop are suspended exactly like dialogue while it is active.</summary>
    public bool BattleActive { get; set; }

    /// <summary>Sets the facing direction after checking its range.</summary>
    /// <param name="facing">Direction in disc numbering, 0..7 with 0 facing down.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="facing"/> is outside 0..7.</exception>
    public void SetFacing(int facing)
    {
        if (facing is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(facing));
        }

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
