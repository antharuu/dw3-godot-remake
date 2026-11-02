using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks NPCs in S200, S210 and S235: decoded NPC texels are visible, sort-Y occlusion against map
/// objects works in both directions, a held step into an NPC occupancy diamond is refused, and
/// re-entering a zone keeps one sprite per NPC. Every expected texel comes from the decoded PNGs.
/// </summary>
internal sealed class NpcProbe : FieldProbe
{
    /// <summary>Zones whose first unflipped NPC must render visibly.</summary>
    private static readonly string[] VisibleNpcZones = ["S200", "S210", "S235"];

    /// <summary>Initializes a new instance of the <see cref="NpcProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public NpcProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "NPC_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory();
        var npc = Host.Npc ?? throw new InvalidOperationException("npc data unavailable");
        if (Host.PlayerData is null)
        {
            throw new InvalidOperationException("renderer unavailable");
        }

        var present = new Dictionary<string, int>();
        var pixelMatches = new Dictionary<string, int>();
        foreach (var zoneId in VisibleNpcZones)
        {
            if (!npc.Zones.TryGetValue(zoneId, out var records) || records.Count == 0)
            {
                throw new InvalidOperationException($"no visible NPC records for {zoneId}");
            }

            present[zoneId] = records.Count;
            var nonFlipped = records.FirstOrDefault(record => !record.FlipH) ?? records[0];
            var matches = await VerifyNpcPixelsAsync(captureRoot, zoneId, nonFlipped);
            pixelMatches[zoneId] = matches;
            Require(matches > 40, $"zone={zoneId} npc={nonFlipped.ActorId} rendered too few source pixels matches={matches}");
        }

        // The same S235 NPC (actor1, sort Y 192) stands in front of object 11 (sort Y 167) and
        // behind object 9 (sort Y 200), both depth-4 map objects.
        var frontMatches = await VerifyNpcOcclusionAsync(captureRoot, "S235", "actor1", 11, npcInFront: true);
        var backMatches = await VerifyNpcOcclusionAsync(captureRoot, "S235", "actor1", 9, npcInFront: false);
        Require(frontMatches > 10, $"S235 NPC-in-front occlusion foreground mismatch matches={frontMatches}");
        Require(backMatches > 10, $"S235 NPC-behind occlusion foreground mismatch matches={backMatches}");
        AssertNpcCollision("S200", "actor28");
        var reenteredCount = AssertSingleSpritePerNpcOnReentry(present["S200"]);
        QuietLog.Info($"npc probe PASS zones=S200,S210,S235 npcs={string.Join(",", present.Select(kv => $"{kv.Key}={kv.Value}"))} pixels={string.Join(",", pixelMatches.Select(kv => $"{kv.Key}={kv.Value}"))} occlusion_front={frontMatches} occlusion_back={backMatches} reentry={reenteredCount}");
        ProbeMemory.CollectBeforeQuit();
    }

    /// <summary>Frames an NPC with the player offset away from it and counts its visible texels.</summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <param name="zoneId">Zone to load.</param>
    /// <param name="npc">NPC to frame.</param>
    /// <returns>The number of idle frame 0 texels visible on screen.</returns>
    private async Task<int> VerifyNpcPixelsAsync(string captureRoot, string zoneId, NpcRecord npc)
    {
        Host.ApplyScene(Host.GetOrLoadZone(zoneId));
        Host.State.ZoneId = zoneId;

        // The camera follows the player, so the player stands 60 units away to keep the NPC framed
        // without covering it.
        Host.State.Position = new NumericsVector2(npc.X + 60, npc.Y + 60);
        Host.State.SetFacing(1);
        var capture = await ViewportCapture.PresentAsync(Host);
        var path = Path.Combine(captureRoot, $"npc-{zoneId}-{npc.ActorId}.png");
        ViewportCapture.Save(capture, path, $"npc capture write failed={path}");
        return new NpcPixels(Host).CountSourceMatches(capture, npc, npc.IdleFrames[0]);
    }

    /// <summary>Frames an NPC and a map object together and counts overlap pixels showing the expected sprite.</summary>
    /// <param name="captureRoot">Capture directory.</param>
    /// <param name="zoneId">Zone to load.</param>
    /// <param name="npcActorId">NPC actor name.</param>
    /// <param name="objectIndex">Map object index in the zone.</param>
    /// <param name="npcInFront"><see langword="true"/> when the NPC must cover the object.</param>
    /// <returns>The number of matching overlap pixels.</returns>
    private async Task<int> VerifyNpcOcclusionAsync(string captureRoot, string zoneId, string npcActorId, int objectIndex, bool npcInFront)
    {
        var npc = Host.Npc!.Zones[zoneId].Single(record => record.ActorId == npcActorId);
        var waterObject = Host.Water!.Zones[zoneId].Single(item => item.Index == objectIndex);
        Host.ApplyScene(Host.GetOrLoadZone(zoneId));
        Host.State.ZoneId = zoneId;
        Host.State.Position = new NumericsVector2((npc.X + waterObject.X) / 2f, ((npc.Y + waterObject.Y) / 2f) + 40);
        Host.State.SetFacing(1);
        var capture = await ViewportCapture.PresentAsync(Host);
        var path = Path.Combine(captureRoot, $"npc-occlusion-{zoneId}-{npcActorId}-obj{objectIndex}-{(npcInFront ? "front" : "back")}.png");
        ViewportCapture.Save(capture, path, $"npc occlusion capture write failed={path}");
        return new NpcPixels(Host).CountObjectOverlapMatches(capture, npc, waterObject, npcInFront);
    }

    /// <summary>
    /// Tries the four axis approaches to an NPC from walkable floor and requires one held step to be
    /// refused by that NPC occupancy diamond.
    /// </summary>
    /// <param name="zoneId">Zone of the NPC.</param>
    /// <param name="npcActorId">NPC actor name.</param>
    /// <exception cref="InvalidOperationException">No approach produced an actor rejection.</exception>
    private void AssertNpcCollision(string zoneId, string npcActorId)
    {
        var zone = Host.GetOrLoadZone(zoneId);
        var occupant = zone.Occupants.Single(item => item.Name == npcActorId);
        var half = occupant.Width / 2f;
        (NumericsVector2 Pos, SemanticDirection Dir)[] approaches =
        [
            (new NumericsVector2(occupant.Position.X, occupant.Position.Y - half - 2), SemanticDirection.Down),
            (new NumericsVector2(occupant.Position.X, occupant.Position.Y + half + 2), SemanticDirection.Up),
            (new NumericsVector2(occupant.Position.X - half - 2, occupant.Position.Y), SemanticDirection.Right),
            (new NumericsVector2(occupant.Position.X + half + 2, occupant.Position.Y), SemanticDirection.Left),
        ];
        foreach (var (approach, direction) in approaches.Where(item => zone.Floor.Contains(item.Pos) && zone.Floor.CellAt(item.Pos) != 0))
        {
            var rejection = HoldStepFrom(zone, approach, direction);
            QuietLog.Info($"npc collision attempt zone={zoneId} npc={npcActorId} approach=({approach.X:0},{approach.Y:0}) direction={direction} rejection={rejection}");
            if (rejection == $"actor:{npcActorId}")
            {
                QuietLog.Info($"npc collision PASS zone={zoneId} npc={npcActorId} approach=({approach.X:0},{approach.Y:0}) direction={direction} rejection={rejection}");
                return;
            }
        }

        throw new InvalidOperationException($"no walkable axis approach to NPC {zoneId}.{npcActorId} produced an actor rejection");
    }

    /// <summary>Places the player and holds one direction for a single step.</summary>
    /// <param name="zone">Zone to apply.</param>
    /// <param name="approach">Start position.</param>
    /// <param name="direction">Held direction.</param>
    /// <returns>The locomotion rejection reason, or <see langword="null"/> when the step was taken.</returns>
    private string? HoldStepFrom(FieldZone zone, NumericsVector2 approach, SemanticDirection direction)
    {
        Host.State.ZoneId = zone.Zone.Id;
        Host.State.Position = approach;
        Host.ApplyScene(zone);
        Host.SetTestHeldDirection(direction);
        MovementResult result = default;
        Host.SimulateStep(movement => result = movement);
        Host.SetTestHeldDirection(SemanticDirection.None);
        return result.Rejection;
    }

    /// <summary>Applies S200, S235 then S200 again and requires the NPC sprite count to stay at the record count.</summary>
    /// <param name="expected">Number of S200 NPC records.</param>
    /// <returns>The sprite count after re-entry.</returns>
    private int AssertSingleSpritePerNpcOnReentry(int expected)
    {
        var s200 = Host.GetOrLoadZone("S200");
        Host.ApplyScene(s200);
        var firstCount = Host.Renderer.NpcSpriteCount;
        Host.ApplyScene(Host.GetOrLoadZone("S235"));
        Host.ApplyScene(s200);
        var reenteredCount = Host.Renderer.NpcSpriteCount;
        Require(
            firstCount == reenteredCount && reenteredCount == expected,
            $"re-entry duplicated NPC sprites first={firstCount} reentered={reenteredCount} expected={expected}");
        return reenteredCount;
    }
}
