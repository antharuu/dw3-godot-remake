namespace Dw3Production.Presentation;

/// <summary>
/// The source screen fade of battle scripts: an additive full-screen white quad whose level rises by
/// <c>255 / frames</c> per frame up to 255 and holds, then after the end command falls by
/// <c>255 / frames</c> per frame from its current level until it is below zero and removed.
/// </summary>
internal sealed class BattleWhiteFlash
{
    /// <summary>Full white.</summary>
    public const int FullLevel = 0xFF;

    private int _speed;

    /// <summary>Initializes a new instance of the <see cref="BattleWhiteFlash"/> class (script fade command 0).</summary>
    /// <param name="frames">Source frames to reach full white.</param>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public BattleWhiteFlash(int frames) => _speed = Speed(frames);

    /// <summary>Gets the additive white level, 0 to 255.</summary>
    public int Level { get; private set; }

    /// <summary>Gets the current phase.</summary>
    public BattleWhiteFlashPhase Phase { get; private set; } = BattleWhiteFlashPhase.Rising;

    /// <summary>Starts fading back (script fade command 1).</summary>
    /// <param name="frames">Source frames from full white to black.</param>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public void End(int frames)
    {
        _speed = Speed(frames);
        if (Phase != BattleWhiteFlashPhase.Ended)
        {
            Phase = BattleWhiteFlashPhase.Falling;
        }
    }

    /// <summary>Advances one source frame.</summary>
    public void Step()
    {
        if (Phase == BattleWhiteFlashPhase.Rising)
        {
            Level += _speed;
            if (Level >= FullLevel)
            {
                Level = FullLevel;
                Phase = BattleWhiteFlashPhase.Holding;
            }
        }
        else if (Phase == BattleWhiteFlashPhase.Falling)
        {
            Level -= _speed;
            if (Level < 0)
            {
                Level = 0;
                Phase = BattleWhiteFlashPhase.Ended;
            }
        }
    }

    /// <summary>Computes the source integer speed.</summary>
    /// <param name="frames">Positive duration.</param>
    /// <returns><c>255 / frames</c>, truncated.</returns>
    private static int Speed(int frames) => frames > 0 ? FullLevel / frames : throw new ArgumentOutOfRangeException(nameof(frames), "A white flash needs a positive duration.");
}
