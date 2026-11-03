using System.Numerics;

namespace Dw3Production.Presentation;

/// <summary>Converts fixed-point battle-scene values into Godot coordinates and camera parameters.
/// The helpers remain engine-free so placement and projection invariants can be tested without a scene tree.</summary>
public static class BattleSceneMath
{
    /// <summary>Number of signed scene-coordinate units represented by one Godot world unit.</summary>
    public const float Ps1UnitsPerGodotUnit = 256f;

    /// <summary>Unsigned scene distance from the origin to either fighter's neutral camp, in fixed-point units.</summary>
    public const int CampOrigin = 0x1400;

    /// <summary>Half of the 240-line source screen height, in projection-plane pixels.</summary>
    public const float Ps1ScreenHalfHeight = 120f;

    /// <summary>Calculates the integer increase used to finish a stage fade within its authored duration.</summary>
    /// <param name="fadeInTime">Positive duration in PAL frames.</param>
    /// <returns>Whole 12-bit fade units to add per PAL frame.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fadeInTime"/> is zero or negative.</exception>
    public static int StageFadeStep(int fadeInTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fadeInTime);
        return 0x1000 / fadeInTime;
    }

    /// <summary>Advances a 12-bit stage fade by complete PAL frames without exceeding full brightness.</summary>
    /// <param name="fade">Current fade amount in the inclusive 0..4096 range.</param>
    /// <param name="step">Fade units applied per PAL frame.</param>
    /// <param name="palFrames">Elapsed frame count. Negative values contribute no progress.</param>
    /// <returns>The updated fade amount, capped at 4096.</returns>
    public static int AdvanceStageFade(int fade, int step, int palFrames) =>
        Math.Min(0x1000, fade + step * Math.Max(0, palFrames));

    /// <summary>Interpolates one signed fixed-point vector component with a 12-bit blend fraction.</summary>
    /// <param name="from">Component value at blend fraction zero.</param>
    /// <param name="to">Component value at blend fraction 4096.</param>
    /// <param name="t">12-bit blend fraction. Values outside the usual 0..4096 range extrapolate.</param>
    /// <returns>The truncated integer interpolation result.</returns>
    public static int LerpFixed(int from, int to, int t) => from + ((to - from) * t >> 12);

    /// <summary>Builds the bone orientation and scale matrix from 4096-units-per-turn rotations and 12-bit scales.</summary>
    /// <param name="rotationFixed">X, Y, and Z rotations expressed in 4096 units per full turn.</param>
    /// <param name="scaleFixed">Per-axis scale where 4096 represents one.</param>
    /// <returns>A row-vector matrix that applies scale followed by X, Y, then Z rotation.</returns>
    public static Matrix4x4 Ps1BoneBasis(Vector3 rotationFixed, Vector3 scaleFixed)
    {
        const float Turn = MathF.Tau / 4096f;
        var scale = Matrix4x4.CreateScale(scaleFixed / 4096f);
        return scale
            * Matrix4x4.CreateRotationX(rotationFixed.X * Turn)
            * Matrix4x4.CreateRotationY(rotationFixed.Y * Turn)
            * Matrix4x4.CreateRotationZ(rotationFixed.Z * Turn);
    }

    /// <summary>Places a fighter at its camp and converts its source axes into Godot world space.</summary>
    /// <param name="enemy"><see langword="true"/> for the opposing camp, otherwise the player camp.</param>
    /// <param name="distance">Additional separation from the neutral camp, in source units.</param>
    /// <param name="height">Vertical source offset, in source units.</param>
    /// <returns>Godot position and yaw that face the two camps toward one another.</returns>
    public static FighterTransform FighterPlacement(bool enemy, int distance, int height)
    {
        var sourceZ = enemy ? CampOrigin + distance : -CampOrigin - distance;
        var sourceY = -height;
        return new FighterTransform(
            new Vector3(0f, -sourceY / Ps1UnitsPerGodotUnit, -sourceZ / Ps1UnitsPerGodotUnit),
            enemy ? 0f : MathF.PI);
    }

    /// <summary>Converts an authored camera translation from source coordinate units into Godot world units.</summary>
    /// <param name="x">Horizontal source translation.</param>
    /// <param name="y">Vertical source translation.</param>
    /// <param name="z">Depth source translation.</param>
    /// <returns>Translation scaled to Godot world units.</returns>
    public static Vector3 CameraPosition(int x, int y, int z) =>
        new Vector3(x, y, z) / Ps1UnitsPerGodotUnit;

    /// <summary>Applies an authored camera rotation to either the position or look-at vector before world conversion.</summary>
    /// <param name="view">Decoded camera record containing translations, rotations, and position vectors.</param>
    /// <param name="reference"><see langword="true"/> to compose the reference vector, otherwise the position vector.</param>
    /// <returns>The rotated and translated vector in Godot world units.</returns>
    public static Vector3 ComposeCameraPosition(SourceBattleCameraView view, bool reference)
    {
        var local = reference
            ? new Vector3(view.Vrx, view.Vry, view.Vrz)
            : new Vector3(view.Vpx, view.Vpy, view.Vpz);
        const float Turn = MathF.Tau / 4096f;
        var rotation = Matrix4x4.CreateRotationY(view.RotationY * Turn)
            * Matrix4x4.CreateRotationX(view.RotationX * Turn)
            * Matrix4x4.CreateRotationZ(view.RotationZ * Turn);
        var transformed = Vector3.Transform(local, rotation) + new Vector3(view.Tx, view.Ty, view.Tz);
        return transformed / Ps1UnitsPerGodotUnit;
    }

    /// <summary>Calculates a vertical field of view for a 240-line source layer.</summary>
    /// <param name="projection">Positive eye-to-projection-plane distance in source pixels.</param>
    /// <returns>Vertical field of view in degrees.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="projection"/> is zero or negative.</exception>
    public static float VerticalFovDegrees(int projection) => VerticalFovDegrees(projection, Ps1ScreenHalfHeight);

    /// <summary>Calculates a vertical field of view for an authored layer of a specified half-height.</summary>
    /// <param name="projection">Positive eye-to-projection-plane distance in source pixels.</param>
    /// <param name="halfHeight">Positive half-height of the target layer, in source pixels.</param>
    /// <returns>Vertical field of view in degrees.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="projection"/> or <paramref name="halfHeight"/> is zero or negative.</exception>
    public static float VerticalFovDegrees(int projection, float halfHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfHeight);
        return 2f * MathF.Atan(halfHeight / projection) * 180f / MathF.PI;
    }

    /// <summary>Selects the authored partner camera for the currently active idle-motion variant.</summary>
    /// <param name="idleMotion">Idle-motion selection. Zero selects the default variant.</param>
    /// <returns>Camera view index six for the default variant, otherwise index seven.</returns>
    public static int PartnerCameraViewIndex(int idleMotion) => idleMotion == 0 ? 6 : 7;

    /// <summary>Position and yaw angle of a fighter after source-to-Godot coordinate conversion.</summary>
    /// <param name="Position">World position in Godot units.</param>
    /// <param name="YawRadians">Rotation around the vertical axis in radians.</param>
    public readonly record struct FighterTransform(Vector3 Position, float YawRadians);
}
