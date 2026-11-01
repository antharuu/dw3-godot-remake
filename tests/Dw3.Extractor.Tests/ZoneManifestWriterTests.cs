using System.Text.Json;
using Dw3.Extractor;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class ZoneManifestWriterTests
{
    [Fact]
    public void WriteIncludesAllRequiredSections()
    {
        var floorCells = new byte[] { 0, 1, 1, 0 };
        var triggerCells = new byte[] { 0, 0, 100, 0 };
        var areasCells = new byte[] { 0, 0, 0, 0 };
        var floorMap = new TmpkMap(2, 2, floorCells);
        var triggerMap = new TmpkMap(2, 2, triggerCells);
        var areasMap = new TmpkMap(2, 2, areasCells);
        var slots = new List<StageSlot>
        {
            new(0xFFFF, 1, 0x202, 776, 224, 1),
            new(0xFFFF, 0, 0, 0, 0, 0),
        };
        var stageModes = new Dictionary<ushort, int> { [0x202] = 205 };
        var tileRecords = new List<ZoneTileRecord>
        {
            new ZoneTileRecord(0, Present: true, Rlen: false, DecodedBytes: 100, TimOffset: 40, TimWidth: 128, TimHeight: 128,
                Sprites: [new ZoneSpriteRecord(0, 0, 0, 0, 0, 128, 128)]),
        };
        var data = new ZoneManifestData(
            Zone: "S200",
            PackPath: "AAA/DAT/Z_STAGE/S200PACK.BIN",
            PackBytes: 1435648,
            PackSha256: "deadbeef",
            TileCount: 1,
            TileColumns: 10,
            TileRows: 7,
            TilePacketBytes: 2048,
            TmpkPath: "AAA/DAT/FIELD/STAGE/S200TMPK.BIN",
            TmpkBytes: 14760,
            TmpkSha256: "cafebabe",
            FloorEntry: 1,
            TriggerEntry: 2,
            AreasEntry: null,
            RequiredOffsets: [12, 2672, 9248],
            FloorMap: floorMap,
            TriggerMap: triggerMap,
            AreasMap: areasMap,
            TileRecords: tileRecords,
            StreamDepth11Modulate: [84, 103, 150],
            Slots: slots,
            StageModes: stageModes,
            Status: "loadable"
        );

        using var output = new StringWriter();
        ZoneManifestWriter.Write(output, data);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;

        Assert.Equal("S200", root.GetProperty("zone").GetString());
        Assert.Equal("loadable", root.GetProperty("status").GetString());
        Assert.Equal(4, root.GetProperty("areas").GetProperty("histogram").GetProperty("0").GetInt32());
        var floorHist = root.GetProperty("floor").GetProperty("histogram");
        Assert.Equal(2, floorHist.GetProperty("0").GetInt32());
        Assert.Equal(2, floorHist.GetProperty("1").GetInt32());
        var trigHist = root.GetProperty("triggers").GetProperty("histogram");
        Assert.Equal(3, trigHist.GetProperty("0").GetInt32());
        Assert.Equal(1, trigHist.GetProperty("100").GetInt32());
        Assert.Equal("AAA/DAT/Z_STAGE/S200PACK.BIN", root.GetProperty("map_source").GetProperty("path").GetString());
        Assert.Equal(1435648, root.GetProperty("map_source").GetProperty("bytes").GetInt32());
        Assert.Equal(2, root.GetProperty("map_source").GetProperty("header").GetProperty("width_tiles").GetInt32());
        Assert.Equal(1, root.GetProperty("map_entries_source").GetProperty("floor_entry").GetInt32());
        Assert.Equal(2, root.GetProperty("map_entries_source").GetProperty("trigger_entry").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("map_entries_source").GetProperty("areas_entry").ValueKind);
        var render = root.GetProperty("render");
        Assert.Equal("composite-back-to-front.png", render.GetProperty("composite").GetString());
        Assert.Equal(3, render.GetProperty("depth_layers").GetArrayLength());
        Assert.Equal(84, render.GetProperty("stream_depth11_modulate")[0].GetInt32());
        Assert.Single(render.GetProperty("tile_records").EnumerateArray());
        Assert.Equal(2, root.GetProperty("slots").GetArrayLength());
        JsonElement exit = Assert.Single(root.GetProperty("exits").EnumerateArray());
        Assert.Equal("S205", exit.GetProperty("destination").GetString());
    }

    [Fact]
    public void WriteKeepsTileGridHeaderWhenAllMapEntriesExist()
    {
        var map = new TmpkMap(2, 2, [0, 0, 0, 0]);
        var data = new ZoneManifestData(
            Zone: "S320",
            PackPath: "AAA/DAT/Z_STAGE/S320PACK.BIN",
            PackBytes: 2048,
            PackSha256: "deadbeef",
            TileCount: 1,
            TileColumns: 9,
            TileRows: 7,
            TilePacketBytes: 2048,
            TmpkPath: "AAA/DAT/FIELD/STAGE/S320TMPK.BIN",
            TmpkBytes: 64,
            TmpkSha256: "cafebabe",
            FloorEntry: 1,
            TriggerEntry: 2,
            AreasEntry: 3,
            RequiredOffsets: [4, 8, 12, 16],
            FloorMap: map,
            TriggerMap: map,
            AreasMap: map,
            TileRecords: [],
            StreamDepth11Modulate: null,
            Slots: [],
            StageModes: new Dictionary<ushort, int>(),
            Status: "loadable");

        using var output = new StringWriter();
        ZoneManifestWriter.Write(output, data);
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement header = document.RootElement.GetProperty("map_source").GetProperty("header");

        Assert.Equal(9, header.GetProperty("width_tiles").GetInt32());
        Assert.Equal(7, header.GetProperty("height_tiles").GetInt32());
    }
}
