using System.Text.Json;
using Dw3.Data;
using Dw3Production.Conversion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class FieldDataPackLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-field-pack-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsFieldRuntimeFromValidatedPack()
    {
        BuildPack();

        using var loader = new FieldDataPackLoader(new DataPackReader(_root));

        Assert.Equal(["S200", "S235"], loader.ZoneIds);
        Zone zone = loader.LoadZone("S200");
        Assert.Equal((32, 24), (zone.Width, zone.Height));
        Assert.Equal([7, 3, 11], zone.Layers.Select(layer => layer.SourceDepth));
        FieldExit exit = Assert.Single(zone.Exits);
        Assert.Equal((4, "S235", 278, 218, 3), (exit.Slot, exit.Destination, exit.X, exit.Y, exit.Facing));
        Assert.True(exit.IsUnevaluated);
        Assert.Single(zone.Climbs);
        Assert.Single(zone.Drops!);

        PlayerData player = loader.LoadPlayer();
        Assert.Equal((-16, -28), (player.AnchorX, player.AnchorY));
        Assert.Equal(12, Assert.Single(player.Sequences).Records[0].Frame);
        Assert.EndsWith("player/frames/frame-0C.png", loader.PlayerFramePath(12).Replace('\\', '/'));

        NpcData npcs = loader.LoadNpcs();
        NpcRecord npc = Assert.Single(npcs.Zones["S200"]);
        Assert.Equal("actor0", npc.ActorId);
        Assert.Equal((3, true), (npc.SpriteDirection, npc.FlipH));
        NpcFrame frame = Assert.Single(npc.IdleFrames);
        Assert.Equal((2, -5, 6), (frame.OffsetX, frame.OffsetY, frame.Duration));
        Assert.Single(npcs.Occupants);
    }

    [Fact]
    public void RejectsAZoneWhosePackFilesAreMissing()
    {
        BuildPack();
        File.Delete(Path.Combine(_root, "data", "zones", "S200", "trigger-cells.png"));

        DataPackCorrupt error = Assert.Throws<DataPackCorrupt>(() => new FieldDataPackLoader(new DataPackReader(_root)));

        Assert.Contains("trigger", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsMalformedDepthModulate()
    {
        BuildPack();
        string manifest = Path.Combine(_root, "data", "zones", "S200", "manifest.json");
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest)))
        {
            var malformed = new
            {
                map_entries_source = new { areas_entry = 3 },
                map_source = new { sha256 = "synthetic-zone" },
                render = new { stream_depth11_modulate = new[] { 256, 0, 0 } },
                slots = Array.Empty<object>(),
            };
            File.WriteAllText(manifest, JsonSerializer.Serialize(malformed));
        }

        using var loader = new FieldDataPackLoader(new DataPackReader(_root));
        CacheResourceException error = Assert.Throws<CacheResourceException>(() => loader.LoadZone("S200"));
        Assert.Contains("CACHE_INVALID", error.Message, StringComparison.Ordinal);
    }

    private void BuildPack()
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                foreach (string relative in new[]
                {
                    "zones/S200/depth-0.png", "zones/S200/depth-1.png", "zones/S200/depth-2.png",
                    "zones/S200/floor-cells.png", "zones/S200/trigger-cells.png", "zones/S200/areas-cells.png",
                    "player/frames/frame-0C.png", "npc/200/actor-0/frames/frame-09.png",
                })
                {
                    string path = context.GetImagePath(relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, [1, 2, 3]);
                    context.RegisterImage(relative);
                }

                WriteJson(context.GetImagePath("zones/S200/manifest.json"), new
                {
                    map_entries_source = new { areas_entry = 3 },
                    map_source = new { sha256 = "synthetic-zone" },
                    render = new { stream_depth11_modulate = new[] { 128, 64, 255 } },
                    slots = new object[]
                    {
                        new { arg = 2, condition = 0xFFFF, dir = 1, type = 2, x = 7, y = 8 },
                        new { arg = 3, condition = 0xFFFF, dir = 1, type = 4, x = 9, y = 10 },
                    },
                });
                WriteJson(context.GetImagePath("player/manifest.json"), new
                {
                    player = new
                    {
                        render_anchor = new { offset = new[] { -16, -28 } },
                        sets = new[]
                        {
                            new
                            {
                                set = 1,
                                sequences = new[]
                                {
                                    new
                                    {
                                        direction = 0,
                                        records = new[]
                                        {
                                            new
                                            {
                                                duration = 4,
                                                foot_ground_contact = new[] { 0.0, 0.0 },
                                                frame = 12,
                                                offset = new[] { 2, -5 },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                });

                Execute(context, "INSERT INTO zones VALUES (200,1,1,32,24),(235,1,1,40,30)");
                Execute(context, "INSERT INTO field_stages VALUES (200,512,1,'loadable',NULL),(235,513,2,'loadable',NULL)");
                Execute(context, "INSERT INTO field_warps VALUES (200,4,513,235,278,218,3,12,'unevaluated')");
                Execute(context, "INSERT INTO npc_actors VALUES (200,0,21,1,10,20,5,24,0,1,1,2,'AAA/DAT/FIELD/SPRT/TEST.BIN')");
                Execute(context, "INSERT INTO npc_characters VALUES (21,200,0,'synthetic','AAA/DAT/FIELD/SPRT/TEST.BIN','aa',3,2,1)");
                Execute(context, "INSERT INTO npc_anim_sets VALUES (21,200,0,1,3,3,3,3,3)");
                Execute(context, "INSERT INTO npc_sequences VALUES (21,200,0,3,'loop')");
                Execute(context, "INSERT INTO npc_sequence_records VALUES (21,200,0,3,0,6,9,2,-5)");
                Execute(context, "INSERT INTO npc_frames VALUES (21,200,0,9,16,24,'npc/200/actor-0/frames/frame-09.png')");
            });
    }

    private static void Execute(DataPackWriteContext context, string sql)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value));
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
