using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Loads one private, qualified draw snapshot without combining unrelated captures.</summary>
internal sealed class BattleCameraSnapshot
{
    private readonly JsonElement _data;
    private readonly string _directory;

    /// <summary>Initializes a new instance of the <see cref="BattleCameraSnapshot"/> class.</summary>
    /// <param name="path">Private manifest path supplied by the operator.</param>
    internal BattleCameraSnapshot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new InvalidDataException("snapshot directory is unavailable");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        _data = document.RootElement.Clone();
        Validate();
        foreach (var role in new[] { "full", "repeat", "without_main", "restored" })
        {
            Require(_data.GetProperty("images").TryGetProperty(role, out _), $"snapshot image role missing: {role}");
        }

        foreach (var image in _data.GetProperty("images").EnumerateObject())
        {
            var bytes = File.ReadAllBytes(ImagePath(image.Name));
            var expected = image.Value.GetProperty("sha256").GetString();
            Require(Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase), "snapshot image digest mismatch");
        }
    }

    /// <summary>Gets the captured fighter identifier.</summary>
    internal int Fighter => _data.GetProperty("fighter").GetInt32();

    /// <summary>Gets a value indicating whether the observed actor belongs to the natural enemy camp.</summary>
    internal bool Enemy => _data.GetProperty("camp").GetString() == "enemy";

    /// <summary>Gets the actual pose index observed in the source model task.</summary>
    internal int PoseFrame => _data.GetProperty("pose_frame").GetInt32();

    /// <summary>Gets the held source actor root, before world-axis conversion.</summary>
    internal Vector3 Root => Vector(Read(_data, "root", 3));

    /// <summary>Gets the independent root projection from the observed draw matrix.</summary>
    internal Vector2 ProjectedRoot => Project(Root);

    /// <summary>Gets the observed world-space bone matrices for pose verification.</summary>
    internal IEnumerable<int[]> Bones => _data.GetProperty("bones").EnumerateArray().Select(value => value.EnumerateArray().Select(item => item.GetInt32()).ToArray());

    /// <summary>Gets independently observed bone visibility, including hidden stale matrices.</summary>
    internal IEnumerable<bool> BoneVisibility => _data.GetProperty("bone_visibility").EnumerateArray().Select(value => value.GetBoolean());

    /// <summary>Converts live source vectors and rotations into the existing authored-camera API.</summary>
    /// <returns>The same physical view, not a newly selected command-menu shot.</returns>
    internal SourceBattleCameraView Camera()
    {
        var camera = _data.GetProperty("camera");
        var position = Read(camera, "position", 3);
        var reference = Read(camera, "reference", 3);
        var translation = Read(camera, "translation", 3);
        var rotation = Read(camera, "rotation", 3);
        Require(rotation[0] == 0 && rotation[2] == 0 && camera.GetProperty("roll").GetInt32() == 0, "snapshot camera rotation is not qualified");

        // Conjugating a Y rotation by C=(x,-y,-z) negates its angle.
        return new SourceBattleCameraView
        {
            Vpx = position[0],
            Vpy = -position[1],
            Vpz = -position[2],
            Vrx = reference[0],
            Vry = -reference[1],
            Vrz = -reference[2],
            Tx = translation[0],
            Ty = -translation[1],
            Tz = -translation[2],
            RotationY = -rotation[1],
            Projection = camera.GetProperty("projection").GetInt32(),
        };
    }

    /// <summary>Projects a source point through the actual held GTE view, independently of look-at math.</summary>
    /// <param name="point">Source world point.</param>
    /// <returns>Projection in the captured logical raster.</returns>
    internal Vector2 Project(Vector3 point)
    {
        var draw = _data.GetProperty("draw_view");
        var rotation = Read(draw, "rotation", 9);
        var translation = Read(draw, "translation", 3);
        var input = new[] { point.X, point.Y, point.Z };
        var transformed = Enumerable.Range(0, 3).Select(row => translation[row] + MathF.Floor(Enumerable.Range(0, 3).Sum(column => rotation[(row * 3) + column] * input[column]) / 4096f)).ToArray();
        Require(transformed[2] > 0, "snapshot root is behind the observed draw view");
        var projection = draw.GetProperty("projection").GetInt32();
        return new Vector2(160f + (projection * transformed[0] / transformed[2]), 120f + (projection * transformed[1] / transformed[2]));
    }

    /// <summary>Recovers the physical camera origin by inverting the actual fixed-point draw view.</summary>
    /// <returns>Camera origin in converted world units.</returns>
    internal Vector3 DrawCameraOrigin()
    {
        var draw = _data.GetProperty("draw_view");
        var rotation = Read(draw, "rotation", 9).Select(value => value / 4096f).ToArray();
        var translation = Read(draw, "translation", 3);
        var matrix = new Matrix4x4(
            rotation[0],
            rotation[3],
            rotation[6],
            0,
            rotation[1],
            rotation[4],
            rotation[7],
            0,
            rotation[2],
            rotation[5],
            rotation[8],
            0,
            translation[0],
            translation[1],
            translation[2],
            1);
        Require(Matrix4x4.Invert(matrix, out var inverse), "snapshot draw view is singular");
        var origin = inverse.Translation;
        return new Vector3(origin.X, -origin.Y, -origin.Z) / BattleSceneMath.Ps1UnitsPerGodotUnit;
    }

    /// <summary>Reads one independently observed view direction in converted world axes.</summary>
    /// <param name="row">Right, down or forward row index.</param>
    /// <returns>A normalized direction, retaining the source row meaning.</returns>
    internal Vector3 DrawViewDirection(int row)
    {
        var rotation = Read(_data.GetProperty("draw_view"), "rotation", 9);
        var first = row * 3;
        return Vector3.Normalize(new Vector3(rotation[first], -rotation[first + 1], -rotation[first + 2]));
    }

    /// <summary>Resolves an image belonging to this manifest.</summary>
    /// <param name="name">Named capture role.</param>
    /// <returns>Absolute image path.</returns>
    internal string ImagePath(string name) => Path.Combine(_directory, _data.GetProperty("images").GetProperty(name).GetProperty("path").GetString() ?? throw new InvalidDataException("snapshot image path is missing"));

    /// <summary>Reads an exact-length integer vector.</summary>
    /// <param name="parent">Containing JSON object.</param>
    /// <param name="name">Property name.</param>
    /// <param name="length">Required component count.</param>
    /// <returns>Validated components.</returns>
    private static int[] Read(JsonElement parent, string name, int length)
    {
        var values = parent.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Require(values.Length == length, $"snapshot vector length mismatch: {name}");
        return values;
    }

    /// <summary>Creates a vector from three validated components.</summary>
    /// <param name="values">Three components.</param>
    /// <returns>The corresponding source vector.</returns>
    private static Vector3 Vector(int[] values) => new(values[0], values[1], values[2]);

    /// <summary>Rejects a failed snapshot contract.</summary>
    /// <param name="condition">Required invariant.</param>
    /// <param name="message">Executable failure boundary.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    /// <summary>Checks source qualification and rejects unsupported or incomplete snapshots.</summary>
    private void Validate()
    {
        Require(_data.GetProperty("schema").GetInt32() == 1 && _data.GetProperty("qualified").GetBoolean(), "snapshot is not qualified");
        Require(Read(_data, "raster", 2).SequenceEqual([320, 240]), "snapshot raster mismatch");
        Require(Math.Abs(_data.GetProperty("fade").GetSingle() - 1f) < 0.0001f && PoseFrame >= 0, "snapshot pose/fade is not held");
        Require(_data.GetProperty("camp").GetString() is "player" or "enemy", "snapshot camp is invalid");
        var qualification = _data.GetProperty("qualification");
        foreach (var field in new[] { "control_changed", "restore_changed", "partner_changed", "hud_changed", "changed_outside_actor_hulls", "nonmain_gte_changed" })
        {
            Require(qualification.GetProperty(field).GetInt32() == 0, $"snapshot drift: {field}");
        }

        Require(qualification.GetProperty("held_camera_and_pose").GetBoolean() && qualification.GetProperty("main_changed").GetInt32() > 0, "snapshot actor isolation is absent");
        Require(_data.GetProperty("camera").GetProperty("projection").GetInt32() == _data.GetProperty("draw_view").GetProperty("projection").GetInt32(), "snapshot projection mismatch");
        Require(Camera().Projection > 0, "snapshot projection is invalid");
        var saved = _data.GetProperty("projected_root").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        Require(saved.Length == 2 && Vector2.Distance(ProjectedRoot, new Vector2(saved[0], saved[1])) < 0.01f, "snapshot draw/root projection mismatch");
    }
}
