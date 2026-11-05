using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Dw3Production.Diagnostics.Battle;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleCameraSnapshotTests
{
    [Fact]
    public void MissingImageRolesCannotQualifyASnapshot()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, SyntheticSnapshot().ToJsonString());
            Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void QualifiedBundleConvertsLiveCameraWithoutUsingTheDrawMatrixAsItsCamera()
    {
        WithManifest(_ => { }, path =>
        {
            var snapshot = new BattleCameraSnapshot(path);
            var camera = snapshot.Camera();
            Assert.Equal(500, camera.Vpy);
            Assert.Equal(2000, camera.Vpz);
            Assert.Equal(-128, camera.RotationY);
            Assert.Equal(200, camera.Projection);
            Assert.Equal(new Vector2(170, 140), snapshot.Project(new Vector3(100, 200, 0)));
            Assert.Equal(new Vector2(160, 120), snapshot.ProjectedRoot);
            Assert.Equal(new Vector3(0, 0, 7.8125f), snapshot.DrawCameraOrigin());
            Assert.Equal(Vector3.UnitX, snapshot.DrawViewDirection(0));
            Assert.Equal(-Vector3.UnitY, snapshot.DrawViewDirection(1));
            Assert.Equal(-Vector3.UnitZ, snapshot.DrawViewDirection(2));
            Assert.Equal(17, snapshot.Fighter);
            Assert.Equal(2, snapshot.PoseFrame);
            Assert.Single(snapshot.Bones);
            Assert.Equal([true], snapshot.BoneVisibility);
        });
    }

    [Theory]
    [InlineData("control_changed")]
    [InlineData("restore_changed")]
    [InlineData("partner_changed")]
    [InlineData("hud_changed")]
    [InlineData("changed_outside_actor_hulls")]
    [InlineData("nonmain_gte_changed")]
    public void SourceDriftIsRejected(string field) =>
        WithManifest(data => data["qualification"]![field] = 1, path => Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path)));

    [Fact]
    public void ChangedImageDigestIsRejected() =>
        WithManifest(data => data["images"]!["full"]!["sha256"] = new string('0', 64), path => Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path)));

    [Fact]
    public void UnqualifiedSnapshotIsRejected() =>
        WithManifest(data => data["qualified"] = false, path => Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path)));

    [Fact]
    public void MismatchedProjectionIsRejected() =>
        WithManifest(data => data["camera"]!["projection"] = 300, path => Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path)));

    [Fact]
    public void MixedRootProjectionIsRejected() =>
        WithManifest(data => data["projected_root"] = new JsonArray(180, 120), path => Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path)));

    [Fact]
    public void UnsupportedRollIsRejected() =>
        WithManifest(data => data["camera"]!["roll"] = 64, path => Assert.Throws<InvalidDataException>(() => new BattleCameraSnapshot(path)));

    [Fact]
    public void MissingManifestIsRejected() => Assert.Throws<ArgumentException>(() => new BattleCameraSnapshot(string.Empty));

    private static void WithManifest(Action<JsonObject> change, Action<string> assertion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "camera-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] bytes = [1, 3, 5, 7];
            File.WriteAllBytes(Path.Combine(directory, "image.bin"), bytes);
            var data = SyntheticSnapshot();
            foreach (var role in new[] { "full", "repeat", "without_main", "restored" })
            {
                data["images"]![role] = new JsonObject { ["path"] = "image.bin", ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)) };
            }

            change(data);
            var path = Path.Combine(directory, "manifest.json");
            File.WriteAllText(path, data.ToJsonString());
            assertion(path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonObject SyntheticSnapshot() => new()
    {
        ["schema"] = 1,
        ["qualified"] = true,
        ["camp"] = "player",
        ["fighter"] = 17,
        ["pose_frame"] = 2,
        ["fade"] = 1,
        ["raster"] = new JsonArray(320, 240),
        ["root"] = new JsonArray(0, 0, 0),
        ["projected_root"] = new JsonArray(160, 120),
        ["images"] = new JsonObject(),
        ["bones"] = new JsonArray(new JsonArray(4096, 0, 0, 0, 4096, 0, 0, 0, 4096, 0, 0, 0)),
        ["bone_visibility"] = new JsonArray(true),
        ["camera"] = new JsonObject
        {
            ["position"] = new JsonArray(0, -500, -2000),
            ["reference"] = new JsonArray(0, 0, 0),
            ["translation"] = new JsonArray(0, 0, 0),
            ["rotation"] = new JsonArray(0, 128, 0),
            ["roll"] = 0,
            ["projection"] = 200,
        },
        ["draw_view"] = new JsonObject
        {
            ["rotation"] = new JsonArray(4096, 0, 0, 0, 4096, 0, 0, 0, 4096),
            ["translation"] = new JsonArray(0, 0, 2000),
            ["projection"] = 200,
        },
        ["qualification"] = new JsonObject
        {
            ["control_changed"] = 0,
            ["restore_changed"] = 0,
            ["partner_changed"] = 0,
            ["hud_changed"] = 0,
            ["main_changed"] = 12,
            ["held_camera_and_pose"] = true,
            ["changed_outside_actor_hulls"] = 0,
            ["nonmain_gte_changed"] = 0,
        },
    };
}
