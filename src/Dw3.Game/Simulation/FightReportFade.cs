namespace Dw3Production.Simulation;

/// <summary>
/// A linear fade of the battle report, from 0 (transparent) to <see cref="FullFade"/> (opaque).
/// A fade in climbs by FullFade/duration per frame; a fade out falls twice as fast.
/// </summary>
internal sealed class FightReportFade
{
    /// <summary>The fully opaque level, 1.0 in the PSX 4.12 fixed-point format.</summary>
    public const int FullFade = 4096;

    public int Level { get; private set; }

    private int Step { get; set; }

    private bool Active { get; set; }

    /// <summary>Starts a fade from transparent (fade in) or opaque (fade out).</summary>
    /// <param name="fadeIn">Whether the fade goes towards opaque.</param>
    /// <param name="duration">The fade-in length in frames; a fade out takes half as long.</param>
    public void Start(bool fadeIn, int duration)
    {
        Level = fadeIn ? 0 : FullFade;
        Step = (fadeIn ? 1 : -2) * (FullFade / duration);
        Active = true;
    }

    /// <summary>Advances the fade one frame.</summary>
    /// <returns><see langword="true"/> once the fade has reached its end (and on every later frame).</returns>
    public bool Update()
    {
        if (!Active)
        {
            return true;
        }

        Level += Step;
        if (Step > 0 && Level > FullFade)
        {
            return Finish(FullFade);
        }

        if (Step < 0 && Level < 0)
        {
            return Finish(0);
        }

        return false;
    }

    /// <summary>Clamps the level to the end it overshot and stops the fade.</summary>
    /// <param name="level">The end level, 0 or <see cref="FullFade"/>.</param>
    /// <returns>Always <see langword="true"/>, for <see cref="Update"/> to return.</returns>
    private bool Finish(int level)
    {
        Level = level;
        Active = false;
        return true;
    }
}
