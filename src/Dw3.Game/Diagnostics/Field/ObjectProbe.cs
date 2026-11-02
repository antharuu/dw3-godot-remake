using Dw3Production.Logging;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks that city map objects once missing (black holes in S210 and S255 before their StageTile
/// objects were decoded) are drawn: in five zones the first opaque object must show more than 40
/// of its decoded texels on screen.
/// </summary>
internal sealed class ObjectProbe : FieldProbe
{
    /// <summary>Zones whose map objects are checked.</summary>
    private static readonly string[] Zones = ["S210", "S255", "S202", "S220", "S300"];

    /// <summary>Initializes a new instance of the <see cref="ObjectProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public ObjectProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "OBJECT_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory();
        foreach (var zoneId in Zones)
        {
            await CheckZoneAsync(captureRoot, zoneId);
        }

        ProbeMemory.CollectBeforeQuit();
    }

    /// <summary>Frames the first opaque object of a zone and counts its visible texels.</summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <param name="zoneId">Zone to load.</param>
    /// <returns>A task that completes once the zone is checked.</returns>
    private async Task CheckZoneAsync(string captureRoot, string zoneId)
    {
        if (Host.Water is null || !Host.Water.Zones.TryGetValue(zoneId, out var water) || water.Count == 0)
        {
            throw new InvalidOperationException($"{zoneId} has no decoded map objects");
        }

        Host.State.ZoneId = zoneId;
        Host.ApplyScene(Host.GetOrLoadZone(zoneId));

        // Blend-mode objects (ABR 0-3) draw a semi-transparent frame whose opaque companion is
        // empty, so only an opaque object (ABR below 0) can be matched texel by texel.
        var target = water.First(item => item.Frames[0].Abr < 0);

        // Offset the player so its sprite does not cover the object the camera frames.
        Host.State.Position = new NumericsVector2(target.X + 60, target.Y + 60);
        Host.State.SetFacing(1);
        var capture = await ViewportCapture.PresentAsync(Host);
        var path = Path.Combine(captureRoot, $"objects-{zoneId}.png");
        ViewportCapture.Save(capture, path, $"object capture write failed={path}");
        var matches = new MapObjectPixels(Host).CountOpaqueSourceMatches(capture, target);
        Require(matches > 40, $"{zoneId} object={target.Index} rendered too few opaque source pixels matches={matches}");
        QuietLog.Info($"objects PASS zone={zoneId} objects={water.Count} object={target.Index} matches={matches} capture={path}");
    }
}
