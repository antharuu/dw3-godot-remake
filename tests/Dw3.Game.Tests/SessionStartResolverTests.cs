using System.Numerics;
using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Dw3Production.Startup;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Covers the start-of-session placement: the default spawn, the DW3_START_ZONE id forms, the
/// DW3_START_POS / DW3_START_FACING overrides and the nearest-walkable-cell search, all on
/// fabricated floors.
/// </summary>
public sealed class SessionStartResolverTests
{
    private static readonly string[] ImportedZones = ["S200", "S330"];

    [Fact]
    public void WithoutStartZoneTheDefaultSpawnIsUsed()
    {
        var resolver = Resolver([]);

        var start = resolver.Resolve(_ => throw new InvalidOperationException("no zone should load"));

        Assert.Equal(SessionStartResolver.DefaultZoneId, start.ZoneId);
        Assert.Equal(new Vector2(1000, 236), start.Position);
        Assert.Equal(1, start.Facing);
        Assert.False(start.IsDiagnostic);
    }

    [Theory]
    [InlineData("S330")]
    [InlineData("330")]
    [InlineData("wstag330")]
    [InlineData(" s330 ")]
    public void StartZoneFormsNormalizeToTheImportedId(string raw)
    {
        var resolver = Resolver(new Dictionary<string, string> { ["DW3_START_ZONE"] = raw });

        var start = resolver.Resolve(id => Zone(id, OpenFloor(5, 5)));

        Assert.Equal("S330", start.ZoneId);
        Assert.True(start.IsDiagnostic);
    }

    [Fact]
    public void NonImportedOrInvalidZonesAreRefused()
    {
        var missing = Resolver(new Dictionary<string, string> { ["DW3_START_ZONE"] = "S999" });
        var invalid = Resolver(new Dictionary<string, string> { ["DW3_START_ZONE"] = "SABC" });

        Assert.Contains("not imported", Assert.Throws<CacheResourceException>(() => missing.Resolve(id => Zone(id, OpenFloor(5, 5)))).Message, StringComparison.Ordinal);
        Assert.Contains("not a valid stage id", Assert.Throws<CacheResourceException>(() => invalid.Resolve(id => Zone(id, OpenFloor(5, 5)))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitPositionAndFacingAreKeptWhenInsideTheFloor()
    {
        var resolver = Resolver(new Dictionary<string, string>
        {
            ["DW3_START_ZONE"] = "S330",
            ["DW3_START_POS"] = "3, 4",
            ["DW3_START_FACING"] = "6",
        });

        var start = resolver.Resolve(id => Zone(id, OpenFloor(5, 5)));

        Assert.Equal(new Vector2(3, 4), start.Position);
        Assert.Equal(6, start.Facing);
    }

    [Theory]
    [InlineData("DW3_START_POS", "5,0", "outside floor")]
    [InlineData("DW3_START_POS", "1", "is not x,y")]
    [InlineData("DW3_START_FACING", "8", "is not a direction")]
    public void InvalidOverridesAreRefused(string variable, string value, string expected)
    {
        var resolver = Resolver(new Dictionary<string, string> { ["DW3_START_ZONE"] = "S330", [variable] = value });

        var error = Assert.Throws<CacheResourceException>(() => resolver.Resolve(id => Zone(id, OpenFloor(5, 5))));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WalkableSearchReturnsTheCentreWhenItIsWalkable()
    {
        Assert.Equal(new Vector2(2, 2), SessionStartResolver.FindWalkableSpawn(Zone("S330", OpenFloor(5, 5))));
    }

    [Fact]
    public void WalkableSearchWalksOutwardRingByRing()
    {
        var cells = new byte[25];
        cells[(4 * 5) + 4] = 0x01;
        cells[(0 * 5) + 0] = 0x01;

        // Both corners lie on ring 2; the ring scan visits rows top-down, so (0,0) wins.
        Assert.Equal(new Vector2(0, 0), SessionStartResolver.FindWalkableSpawn(Zone("S330", new FloorMap(5, 5, cells))));
    }

    [Fact]
    public void UnclassifiedSlopeRowsAreNotWalkable()
    {
        var cells = new byte[9];
        Array.Fill(cells, (byte)0x09);

        Assert.Throws<CacheResourceException>(() => SessionStartResolver.FindWalkableSpawn(Zone("S330", new FloorMap(3, 3, cells))));
    }

    private static SessionStartResolver Resolver(Dictionary<string, string> environment) =>
        new(HostStartupOptions.From(name => environment.GetValueOrDefault(name)), ImportedZones);

    private static FloorMap OpenFloor(int width, int height)
    {
        var cells = new byte[width * height];
        Array.Fill(cells, (byte)0x01);
        return new FloorMap(width, height, cells);
    }

    private static FieldZone Zone(string id, FloorMap floor) =>
        new(
            new Zone(id, floor.Width, floor.Height, [], "floor.png", "trigger.png", [], [], "hash"),
            floor,
            new TriggerMap(floor.Width, floor.Height, new byte[floor.Width * floor.Height]),
            []);
}
