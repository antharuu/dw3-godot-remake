using System.Globalization;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Startup;

/// <summary>
/// Chooses where a session begins. Without <c>DW3_START_ZONE</c> the player starts at the
/// game's own S200 spawn. With it, the session starts in the selected imported zone for map
/// inspection: <c>DW3_START_POS</c> and <c>DW3_START_FACING</c> override the placement,
/// otherwise the player lands on the walkable floor cell nearest to the map centre.
/// </summary>
/// <remarks>
/// Every refused selection raises a <see cref="CacheResourceException"/> with the
/// <c>CACHE_RESOURCE_UNAVAILABLE</c> code, which the host turns into exit code 2.
/// </remarks>
internal sealed class SessionStartResolver
{
    /// <summary>Zone where an ordinary session starts.</summary>
    public const string DefaultZoneId = "S200";

    /// <summary>Facing used when none is selected: direction 1 of the eight source directions.</summary>
    private const int DefaultFacing = 1;

    /// <summary>Largest source direction index; directions run 0..7.</summary>
    private const int MaxFacing = 7;

    /// <summary>Error code shared by every refused selection.</summary>
    private const string Unavailable = "CACHE_RESOURCE_UNAVAILABLE";

    /// <summary>Spawn of an ordinary session on S200, in world units.</summary>
    private static readonly NumericsVector2 DefaultPosition = new(1000, 236);

    private readonly HostStartupOptions _options;
    private readonly IReadOnlyCollection<string> _importedZones;

    /// <summary>Initializes a new instance of the <see cref="SessionStartResolver"/> class.</summary>
    /// <param name="options">Startup selection read from the environment.</param>
    /// <param name="importedZones">Normalized ids (<c>Snnn</c>) of the zones present in the data pack.</param>
    public SessionStartResolver(HostStartupOptions options, IReadOnlyCollection<string> importedZones)
    {
        _options = options;
        _importedZones = importedZones;
    }

    /// <summary>
    /// Finds the walkable floor cell nearest to the map centre, scanning square rings of growing
    /// radius, each ring row by row from the top. The classification matches the F3 overlay, so
    /// the player lands on a green cell rather than on void or an unclassified slope.
    /// </summary>
    /// <param name="zone">Zone whose floor is scanned.</param>
    /// <returns>The cell, in world units.</returns>
    /// <exception cref="CacheResourceException">The floor has no walkable cell.</exception>
    public static NumericsVector2 FindWalkableSpawn(FieldZone zone)
    {
        var centerX = zone.Floor.Width / 2;
        var centerY = zone.Floor.Height / 2;
        var maxRadius = Math.Max(zone.Floor.Width, zone.Floor.Height);
        for (var radius = 0; radius <= maxRadius; radius++)
        {
            foreach (var (x, y) in RingCells(centerX, centerY, radius))
            {
                if (IsInside(zone.Floor, x, y) && IsWalkableCell(zone.Floor, x, y))
                {
                    return new NumericsVector2(x, y);
                }
            }
        }

        throw new CacheResourceException(Unavailable, $"zone={zone.Zone.Id} has no walkable floor cell");
    }

    /// <summary>Resolves the starting zone and placement.</summary>
    /// <param name="loadZone">Loads a zone by id; only called for a diagnostic start, whose spawn depends on the floor.</param>
    /// <returns>The start of the session.</returns>
    /// <exception cref="CacheResourceException">The selected zone, position or facing is refused.</exception>
    public SessionStart Resolve(Func<string, FieldZone> loadZone)
    {
        var zoneId = ResolveDiagnosticZone();
        if (zoneId is null)
        {
            return new SessionStart(DefaultZoneId, DefaultPosition, DefaultFacing, IsDiagnostic: false);
        }

        // The facing is validated before the position, so a selection with both values wrong
        // reports the facing first.
        var zone = loadZone(zoneId);
        var facing = ResolveFacing();
        return new SessionStart(zoneId, ResolvePosition(zone), facing, IsDiagnostic: true);
    }

    /// <summary>Lists the cells of the square ring at a Chebyshev distance from a centre, row by row from the top.</summary>
    /// <param name="centerX">Centre column.</param>
    /// <param name="centerY">Centre row.</param>
    /// <param name="radius">Ring distance; 0 yields the centre alone.</param>
    /// <returns>The ring cells, possibly outside the map.</returns>
    private static IEnumerable<(int X, int Y)> RingCells(int centerX, int centerY, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius)
                {
                    yield return (centerX + dx, centerY + dy);
                }
            }
        }
    }

    /// <summary>Tells whether a cell lies on the floor grid.</summary>
    /// <param name="floor">Floor grid.</param>
    /// <param name="x">Cell column.</param>
    /// <param name="y">Cell row.</param>
    /// <returns><see langword="true"/> when the cell can be read.</returns>
    private static bool IsInside(FloorMap floor, int x, int y) =>
        x >= 0 && y >= 0 && x < floor.Width && y < floor.Height;

    /// <summary>
    /// Tells whether a floor cell admits the player: cell 0 is void, and the low nibble minus one
    /// must be one of the seven classified slope rows (0..6).
    /// </summary>
    /// <param name="floor">Floor grid.</param>
    /// <param name="x">Cell column.</param>
    /// <param name="y">Cell row.</param>
    /// <returns><see langword="true"/> for a walkable cell.</returns>
    private static bool IsWalkableCell(FloorMap floor, int x, int y)
    {
        var cell = floor.CellAt(new NumericsVector2(x, y));
        if (cell == 0)
        {
            return false;
        }

        var row = cell & 0x0F;
        row -= row != 0 ? 1 : 0;
        return row is >= 0 and <= 6;
    }

    /// <summary>Removes the <c>WSTAG</c> stage-file prefix or the <c>S</c> zone prefix from a zone selection.</summary>
    /// <param name="value">Trimmed selection.</param>
    /// <returns>The remaining characters, expected to be the stage number.</returns>
    private static string StripStagePrefix(string value)
    {
        if (value.StartsWith("WSTAG", StringComparison.OrdinalIgnoreCase))
        {
            return value[5..];
        }

        return value.StartsWith('S') || value.StartsWith('s') ? value[1..] : value;
    }

    /// <summary>Parses one unsigned decimal coordinate, ignoring surrounding spaces.</summary>
    /// <param name="text">Coordinate text.</param>
    /// <param name="value">Parsed coordinate.</param>
    /// <returns><see langword="true"/> when the text is a plain non-negative integer.</returns>
    private static bool TryParseCoordinate(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Normalizes <c>DW3_START_ZONE</c> (<c>S330</c>, <c>330</c> or <c>WSTAG330</c>, any case) to
    /// an imported zone id.
    /// </summary>
    /// <returns>The zone id, or <see langword="null"/> when no zone is selected.</returns>
    /// <exception cref="CacheResourceException">The value is not a stage number or the zone is not imported.</exception>
    private string? ResolveDiagnosticZone()
    {
        var raw = _options.StartZone;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        var digits = StripStagePrefix(trimmed);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var stage))
        {
            throw new CacheResourceException(Unavailable, $"zone={trimmed} is not a valid stage id");
        }

        var zoneId = $"S{stage:D3}";
        if (!_importedZones.Contains(zoneId))
        {
            throw new CacheResourceException(Unavailable, $"zone={zoneId} not imported");
        }

        return zoneId;
    }

    /// <summary>Reads <c>DW3_START_FACING</c>.</summary>
    /// <returns>The facing, or the default direction 1 when none is selected.</returns>
    /// <exception cref="CacheResourceException">The value is not a direction in 0..7.</exception>
    private int ResolveFacing()
    {
        var raw = _options.StartFacing;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultFacing;
        }

        if (!int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var facing) || facing > MaxFacing)
        {
            throw new CacheResourceException(Unavailable, $"DW3_START_FACING={raw} is not a direction (0..7)");
        }

        return facing;
    }

    /// <summary>Reads <c>DW3_START_POS</c> as <c>x,y</c> world units inside the zone floor.</summary>
    /// <param name="zone">Zone the player starts in.</param>
    /// <returns>The selected position, or the walkable cell nearest to the map centre when none is selected.</returns>
    /// <exception cref="CacheResourceException">The value is not <c>x,y</c> or lies outside the floor.</exception>
    private NumericsVector2 ResolvePosition(FieldZone zone)
    {
        var raw = _options.StartPos;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return FindWalkableSpawn(zone);
        }

        var parts = raw.Split(',');
        if (parts.Length != 2 || !TryParseCoordinate(parts[0], out var x) || !TryParseCoordinate(parts[1], out var y))
        {
            throw new CacheResourceException(Unavailable, $"DW3_START_POS={raw} is not x,y");
        }

        if (!IsInside(zone.Floor, x, y))
        {
            throw new CacheResourceException(Unavailable, $"zone={zone.Zone.Id} position=({x},{y}) outside floor {zone.Floor.Width}x{zone.Floor.Height}");
        }

        return new NumericsVector2(x, y);
    }
}
