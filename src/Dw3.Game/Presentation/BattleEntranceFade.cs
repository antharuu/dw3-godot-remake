using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The fight-stage entrance fade: a 12-bit level (0 = black, 0x1000 = full) advanced over 60 PAL
/// frames from the moment a stage is created, shared by the stage light set, the stage model colour
/// and the background colour.
/// </summary>
internal sealed class BattleEntranceFade
{
    /// <summary>The fully faded-in level.</summary>
    public const int FullLevel = 0x1000;

    private const int FadeFrames = 60;

    private readonly BattlePalClock _clock = new();

    /// <summary>Gets the fade level, 0..0x1000.</summary>
    public int Level { get; private set; }

    /// <summary>Gets the fade level as a 0..1 fraction.</summary>
    public float Fraction => Level / 4096f;

    /// <summary>Gets a value indicating whether the HUD and partner-view overlays may arrive over the fully lit stage.</summary>
    public bool OverlaysVisible => Level >= FullLevel;

    /// <summary>Starts the fade again from black.</summary>
    public void Restart()
    {
        Level = 0;
        _clock.Reset();
    }

    /// <summary>Jumps to the fully faded-in level, keeping the carried clock fraction.</summary>
    public void Complete() => Level = FullLevel;

    /// <summary>Jumps to a measured point of the fade, for a visual probe.</summary>
    /// <param name="fade">Fraction 0..1; out-of-range values are clamped.</param>
    public void SetFraction(float fade)
    {
        Level = (int)(Mathf.Clamp(fade, 0f, 1f) * FullLevel);
        _clock.Reset();
    }

    /// <summary>Advances the fade by presented time.</summary>
    /// <param name="delta">Presented time in seconds.</param>
    /// <returns>True when the level changed and must be applied.</returns>
    public bool Advance(float delta)
    {
        if (Level >= FullLevel)
        {
            return false;
        }

        var frames = _clock.Advance(delta);
        if (frames == 0)
        {
            return false;
        }

        Level = BattleSceneMath.AdvanceStageFade(Level, BattleSceneMath.StageFadeStep(FadeFrames), frames);
        return true;
    }

    /// <summary>
    /// Binds the entrance's effective light set by normalizing every nonzero interpolated direction
    /// before the model-light consumer. A zero fraction remains zero, matching the consumer's safe
    /// early-out. RGB and ambient alone carry the brightness fade, so a direction magnitude cannot
    /// attenuate the directional term a second time.
    /// </summary>
    /// <param name="source">The decoded stage light set.</param>
    /// <returns>The effective set consumed by model lighting.</returns>
    public BattleModel.BattleLightSet Scale(BattleModel.BattleLightSet source)
    {
        var fade = Fraction;
        return new BattleModel.BattleLightSet(
            [.. source.Lights.Select(light => new BattleModel.BattleLight(NormalizeInterpolatedDirection(light.Direction, fade), light.Rgb * fade))],
            source.Ambient * fade);
    }

    /// <summary>The stage model colour: the PS1 colour mode 1 grey ramps from 0 to 0x80.</summary>
    /// <returns>The grey as RGB/255.</returns>
    public Color StageColor()
    {
        var level = BattleSceneMath.LerpFixed(0, 0x80, Level) / 255f;
        return new Color(level, level, level);
    }

    /// <summary>
    /// The screen background colour ramped toward the stage's own colour. Black is written as
    /// 1,1,1 rather than literal zero, as the PS1 screen layer does.
    /// </summary>
    /// <param name="target">The stage background colour.</param>
    /// <returns>The background colour for the current level.</returns>
    public Color Background(Color target)
    {
        var bg = new Color(
            BattleSceneMath.LerpFixed(0, (int)(target.R * 255), Level) / 255f,
            BattleSceneMath.LerpFixed(0, (int)(target.G * 255), Level) / 255f,
            BattleSceneMath.LerpFixed(0, (int)(target.B * 255), Level) / 255f);
        return bg.R + bg.G + bg.B == 0f ? new Color(1f / 255f, 1f / 255f, 1f / 255f) : bg;
    }

    /// <summary>Returns the SDK-equivalent direction after interpolation from the zero vector.</summary>
    /// <param name="direction">Decoded source direction.</param>
    /// <param name="fade">Current entrance fraction.</param>
    /// <returns>A unit direction, or zero when interpolation yields a zero vector.</returns>
    private static Vector3 NormalizeInterpolatedDirection(Vector3 direction, float fade)
    {
        var interpolated = direction * fade;
        return interpolated.LengthSquared() == 0f ? Vector3.Zero : interpolated.Normalized();
    }
}
