using System.Numerics;

namespace Dw3Production.Presentation;

/// <summary>Source-backed PS1 battle coordinate and projection rules, kept engine-free so the
/// placement contract can be tested without booting Godot.</summary>
public static class BattleSceneMath
{
    public const float Ps1UnitsPerGodotUnit = 256f;
    public const int CampOrigin = 0x1400;
    public const float Ps1ScreenHalfHeight = 120f;

    /// <summary>the battle-stage presentation contract uses 12-bit fade units and integer PAL-frame steps.</summary>
    public static int StageFadeStep(int fadeInTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fadeInTime);
        return 0x1000 / fadeInTime;
    }

    /// <summary>the battle-stage presentation contract advanceStageFade: advance by whole PAL frames and clamp at 0x1000.</summary>
    public static int AdvanceStageFade(int fade, int step, int palFrames) =>
        Math.Min(0x1000, fade + step * Math.Max(0, palFrames));

    /// <summary>FIGHTSTG_interp.lerp / gte_gpf12 for one signed vector component.</summary>
    public static int LerpFixed(int from, int to, int t) => from + ((to - from) * t >> 12);

    /// <summary>RotMatrixZYX_gte followed by ScaleMatrix in the decoded mesh contract. System.Numerics uses
    /// row vectors, so the column-vector source product Rz*Ry*Rx*S is represented as S*Rx*Ry*Rz.</summary>
    public static Matrix4x4 Ps1BoneBasis(Vector3 rotationFixed, Vector3 scaleFixed)
    {
        const float turn = MathF.Tau / 4096f;
        var scale = Matrix4x4.CreateScale(scaleFixed / 4096f);
        return scale
            * Matrix4x4.CreateRotationX(rotationFixed.X * turn)
            * Matrix4x4.CreateRotationY(rotationFixed.Y * turn)
            * Matrix4x4.CreateRotationZ(rotationFixed.Z * turn);
    }

    /// <summary>FIGHTSTG_faceModel, converted from PS1 Y-down/Z-forward into Godot
    /// Y-up/Z-back. The player is on positive Godot Z and receives the source half-turn.</summary>
    public static FighterTransform FighterPlacement(bool enemy, int distance, int height)
    {
        var sourceZ = enemy ? CampOrigin + distance : -CampOrigin - distance;
        var sourceY = -height;
        return new FighterTransform(
            new Vector3(0f, -sourceY / Ps1UnitsPerGodotUnit, -sourceZ / Ps1UnitsPerGodotUnit),
            enemy ? 0f : MathF.PI);
    }

    /// <summary>the decoded battle-camera record contract negates FighterInfo Y/Z into GsRVIEW2. The world conversion
    /// negates them back for Godot, leaving the decoded record components unchanged.</summary>
    public static Vector3 CameraPosition(int x, int y, int z) =>
        new Vector3(x, y, z) / Ps1UnitsPerGodotUnit;

    /// <summary>the decoded battle-camera record contract:84-100 constructs a YXZ coordinate from CameraView.rot/trans
    /// and uses it as GsRVIEW2.super. Apply that same local-coordinate composition before the
    /// existing PS1-to-Godot conversion. Neutral camera records remain byte-for-byte equivalent
    /// to CameraPosition(x,y,z).</summary>
    public static Vector3 ComposeCameraPosition(SourceBattleCameraView view, bool reference)
    {
        var local = reference
            ? new Vector3(view.Vrx, view.Vry, view.Vrz)
            : new Vector3(view.Vpx, view.Vpy, view.Vpz);
        const float turn = MathF.Tau / 4096f;
        var rotation = Matrix4x4.CreateRotationY(view.RotationY * turn)
            * Matrix4x4.CreateRotationX(view.RotationX * turn)
            * Matrix4x4.CreateRotationZ(view.RotationZ * turn);
        var transformed = Vector3.Transform(local, rotation) + new Vector3(view.Tx, view.Ty, view.Tz);
        return transformed / Ps1UnitsPerGodotUnit;
    }

    /// <summary>GsSetProjection's H is the eye-to-projection-plane distance in PS1 pixels.
    /// A 240-line source viewport therefore has a 120-pixel half-height.</summary>
    public static float VerticalFovDegrees(int projection) => VerticalFovDegrees(projection, Ps1ScreenHalfHeight);

    /// <summary>Converts a libgs projection distance for the exact height of its target layer.
    /// the auxiliary fighter-view contract creates layer 0x1009 at 100x60, so its half-height is 30 rather than 120.</summary>
    public static float VerticalFovDegrees(int projection, float halfHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(projection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(halfHeight);
        return 2f * MathF.Atan(halfHeight / projection) * 180f / MathF.PI;
    }

    /// <summary>the decoded fighter-camera record contract FIGHTSTG_loadCamera: view 6 normally, view 7 only while
    /// ModelControl.idleMotion is non-zero.</summary>
    public static int PartnerCameraViewIndex(int idleMotion) => idleMotion == 0 ? 6 : 7;

    /// <summary>Position and yaw angle of a fighter after source-to-Godot coordinate conversion.</summary>
    public readonly record struct FighterTransform(Vector3 Position, float YawRadians);
}
