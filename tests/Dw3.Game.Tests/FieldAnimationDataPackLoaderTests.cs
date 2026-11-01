using Dw3.Data;
using Dw3Production.Conversion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Field animation loader tests over a fabricated data pack (no disc bytes).</summary>
public sealed class FieldAnimationDataPackLoaderTests : IDisposable
{
    private static readonly int[] CueFrames = [0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x45, 0x46];
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-field-anim-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsCueIconActionsAndWaterFromPack()
    {
        BuildPack();
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));

        ExitCue cue = loader.LoadExitCue();
        Assert.Equal(CueFrames.Length, cue.Frames.Count);
        Assert.Equal((-8, -29), (cue.Frames[0x0C].OffsetX, cue.Frames[0x0C].OffsetY));
        Assert.EndsWith("cue/frame-0C.png", cue.Frames[0x0C].Path.Replace('\\', '/'));
        Assert.Equal([6, 7, 8, 9, 10], cue.InnerRows.Keys.Order());
        Assert.Equal([(0x10, 22), (0x11, 22)], cue.InnerRows[6]);

        PlayerIconData icon = loader.LoadPlayerIcon();
        Assert.Equal(15, icon.Frames.Count);
        PlayerIconAnimation animation = icon.Animations[1];
        Assert.Equal(1, animation.LoopTarget);
        Assert.Equal([new PlayerIconStep(0x23, 3), new PlayerIconStep(0x24, 4)], animation.Steps);

        ClimbData climb = loader.LoadClimb();
        ClimbSequence up = climb.Sequences[0x1A];
        Assert.True(up.Loops);
        Assert.Equal(new ClimbRecord(2, 5, -3, 4), Assert.Single(up.Records));
        Assert.EndsWith("climb/frames/frame-02.png", loader.ClimbFramePath(2).Replace('\\', '/'));

        ClimbData drop = loader.LoadDrop();
        Assert.Equal([0x16, 0x17, 0x18, 0x19], drop.Sequences.Keys.Order());
        Assert.False(drop.Sequences[0x16].Loops);
        Assert.EndsWith("drop/frames/frame-01.png", loader.DropFramePath(1).Replace('\\', '/'));

        WaterData water = loader.LoadWater();
        WaterObject item = Assert.Single(water.Zones["S200"]);
        Assert.Equal((3, 64, 2, 1048, 211, 246), (item.Index, item.Margin, item.Depth, item.X, item.Y, item.SortY));
        WaterFrame frame = Assert.Single(item.Frames);
        Assert.Equal((1, 2, 1), (frame.OffsetX, frame.OffsetY, frame.Abr));
        Assert.EndsWith("water/S200/object-003-stp.png", frame.SemiPath.Replace('\\', '/'));
    }

    [Fact]
    public void RejectsUnknownActionFrame()
    {
        BuildPack();
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));
        CacheResourceException error = Assert.Throws<CacheResourceException>(() => loader.ClimbFramePath(9));
        Assert.Contains("CACHE_RESOURCE_UNAVAILABLE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsIncompleteDropSets()
    {
        BuildPack(skipDropSet: 0x19);
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));
        CacheResourceException error = Assert.Throws<CacheResourceException>(() => loader.LoadDrop());
        Assert.Contains("CACHE_INVALID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingIconFrame()
    {
        BuildPack(skipIconFrame: 0x31);
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadPlayerIcon());
    }

    [Fact]
    public void RejectsIconLoopTargetOutOfRange()
    {
        BuildPack(iconLoopTarget: 5);
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadPlayerIcon());
    }

    [Fact]
    public void RejectsCueWithoutAllFieldRows()
    {
        BuildPack(skipCueRow: 10);
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadExitCue());
    }

    [Fact]
    public void RejectsWaterObjectWithoutFrames()
    {
        BuildPack(waterFrames: false);
        using var loader = new FieldAnimationDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadWater());
    }

    [Fact]
    public void RejectsPackWhoseRegisteredImageIsMissing()
    {
        BuildPack();
        File.Delete(Path.Combine(_root, "data", "icon", "frame-23.png"));
        Assert.Throws<DataPackCorrupt>(() => new FieldAnimationDataPackLoader(new DataPackReader(_root)));
    }

    private void BuildPack(int skipDropSet = -1, int skipIconFrame = -1, int iconLoopTarget = 1, int skipCueRow = -1, bool waterFrames = true)
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                Execute(context, "INSERT INTO fieldcom_domains VALUES ('cue','FIELDCOM','aa',1),('icon','FIELDCOM','aa',1)");
                foreach (int frame in CueFrames)
                    Execute(context, $"INSERT INTO fieldcom_frames VALUES ('cue',{frame},16,16,-8,-29,0,-1,'{Image(context, $"cue/frame-{frame:X2}.png")}')");
                int[] rowFrames = [0x10, 0x0E, 0x0C, 0x12, 0x45];
                for (int row = 6; row <= 10; row++)
                {
                    if (row == skipCueRow) continue;
                    int first = rowFrames[row - 6];
                    Execute(context, $"INSERT INTO cue_sequences VALUES ('inner','row-{first:X2}',0,{first},22,{row}),('inner','row-{first:X2}',1,{first + 1},22,{row})");
                }
                Execute(context, "INSERT INTO cue_sequences VALUES ('pop','opening',0,50,1,NULL)");
                for (int frame = 0x23; frame <= 0x31; frame++)
                    if (frame != skipIconFrame)
                        Execute(context, $"INSERT INTO fieldcom_frames VALUES ('icon',{frame},16,16,4,-36,0,-1,'{Image(context, $"icon/frame-{frame:X2}.png")}')");
                Execute(context, $"INSERT INTO icon_animations VALUES (0,0),(1,{iconLoopTarget})");
                Execute(context, "INSERT INTO icon_animation_steps VALUES (0,0,35,8),(1,0,35,3),(1,1,36,4)");

                Execute(context, "INSERT INTO action_domains VALUES ('climb',1,1,'s','CLIMB','aa',1),('drop',2,1,'s','DROP','aa',1)");
                Execute(context, $"INSERT INTO action_frames VALUES ('climb',2,8,8,'{Image(context, "climb/frames/frame-02.png")}')");
                Execute(context, $"INSERT INTO action_frames VALUES ('drop',1,8,8,'{Image(context, "drop/frames/frame-01.png")}')");
                Execute(context, "INSERT INTO action_sets VALUES ('climb',26,'CLIMB_UP','loop')");
                Execute(context, "INSERT INTO action_records VALUES ('climb',26,0,5,2,-3,4)");
                for (int set = 0x16; set <= 0x19; set++)
                {
                    if (set == skipDropSet) continue;
                    Execute(context, $"INSERT INTO action_sets VALUES ('drop',{set},'DROP','{(set == 0x16 ? "end" : "loop")}')");
                    Execute(context, $"INSERT INTO action_records VALUES ('drop',{set},0,2,1,0,0)");
                }

                Execute(context, "INSERT INTO water_zones VALUES (200,'a','b','c',1)");
                Execute(context, "INSERT INTO water_objects VALUES (200,3,1,0,64,2,71,2,0,3,6,0,1048,211,246,0,1)");
                if (waterFrames)
                    Execute(context, $"INSERT INTO water_object_frames VALUES (200,3,0,71,0,1,2,1,'{Image(context, "water/S200/object-003-opaque.png")}','{Image(context, "water/S200/object-003-stp.png")}')");
            });
    }

    private static string Image(DataPackWriteContext context, string relative)
    {
        string path = context.GetImagePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        context.RegisterImage(relative);
        return relative;
    }

    private static void Execute(DataPackWriteContext context, string sql)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
