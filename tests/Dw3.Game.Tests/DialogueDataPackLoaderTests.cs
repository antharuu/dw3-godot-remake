using Dw3.Data;
using Dw3Production.Conversion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Dialogue loader tests over a fabricated data pack (no disc bytes).</summary>
public sealed class DialogueDataPackLoaderTests : IDisposable
{
    private static readonly int[] DialogueFrameNumbers = [0, 2, 3, 4, 5, 6, 7];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-dialogue-pack-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsTalksTextsAndFontFromPack()
    {
        BuildPack();
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));

        DialogueData dialogue = loader.LoadDialogue();
        ZoneDialogue zone = dialogue.Zones["S200"];
        Assert.Equal(1, zone.TalkFile);
        DialogueActor actor = Assert.Single(zone.Actors);
        Assert.Equal((12, 30, 40, 2, 16), (actor.Id, actor.X, actor.Y, actor.Dir, actor.Width));
        Assert.Equal(2, actor.Talks.Count);
        DialogueTalk effect = actor.Talks[1];
        Assert.True(effect.Selected);
        Assert.Equal("effect", effect.Kind);
        Assert.Equal([new DialogueCondition(0x6004, 1, "progress")], effect.Conditions);
        Assert.Equal([new DialogueAction(0x0010, 1, "flag_set"), new DialogueAction(0x9000, 2, "start_event")], effect.Actions);
        Assert.Equal(["flag_set", "start_event"], effect.Families);
        Assert.Empty(actor.Talks[0].Conditions);

        IReadOnlyList<TextToken> entry = dialogue.Texts[1][0];
        Assert.Equal(3, entry.Count);
        Assert.True(entry[0].IsGlyph);
        Assert.Equal(0x41, entry[0].Code);
        Assert.True(entry[1].IsIcon);
        Assert.Equal(2, entry[2].Command);
        Assert.Equal([7, 8], entry[2].Args);

        FontData font = dialogue.Font;
        Assert.Equal(14, font.LineHeight);
        Assert.Equal(new GlyphMetric(0, 1, 2, 3, 4, 5, 6, -1, -2, 7), font.Glyphs[4]);
        Assert.Equal(9, font.Style(3).LineHeight);
        Assert.EndsWith("font/font-style-1-page-1.png", font.Page1Path.Replace('\\', '/'));
        Assert.EndsWith("font/font-style-2-palette-3-page-0.png", font.PalettePagePath(2, 3, 0).Replace('\\', '/'));
    }

    [Fact]
    public void LoadsTalkBoxFromPack()
    {
        BuildPack();
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));

        DialogueFrameData frames = loader.LoadDialogueFrame();
        Assert.Equal(7, frames.Frames.Count);
        Assert.Equal((4, false), (frames.Frames[7].ClutFrames, frames.Frames[7].Semi));
        Assert.True(frames.Frames[0].Semi);
        Assert.Equal((222, 238, 255, 0x45, 0x2C57, 12, 62), (frames.Panel.R, frames.Panel.G, frames.Panel.B, frames.Panel.Tpage, frames.Panel.Clut, frames.Panel.W, frames.Panel.H));
        Assert.Equal(4, frames.Layouts.Count);
        TalkBoxLayout layout = frames.Layouts[2];
        Assert.Equal((5, new Vec2I(-16, -16), new Vec2I(125, -38)), (layout.Sprite, layout.SpritePos, layout.Arrow));
        Assert.Equal((-20, -58), (layout.TextX, layout.TextY));
    }

    [Fact]
    public void RejectsTextFileWithWrongEntryCount()
    {
        BuildPack(textEntryCount: 2);
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));
        CacheResourceException error = Assert.Throws<CacheResourceException>(() => loader.LoadDialogue());
        Assert.Contains("CACHE_INVALID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingTextFile()
    {
        BuildPack();
        System.IO.File.Delete(Path.Combine(_root, "data", "text", "talk01.json"));
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));
        CacheResourceException error = Assert.Throws<CacheResourceException>(() => loader.LoadDialogue());
        Assert.Contains("CACHE_RESOURCE_UNAVAILABLE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownTokenType()
    {
        BuildPack(textJson: """{"count":1,"entries":[[{"t":"x"}]]}""");
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadDialogue());
    }

    [Fact]
    public void RejectsFontStyleWithUnexpectedLineHeight()
    {
        BuildPack(style2LineHeight: 12);
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadDialogue());
    }

    [Fact]
    public void RejectsTalkBoxWithoutArrowFrame()
    {
        BuildPack(skipFrame: 7);
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadDialogueFrame());
    }

    [Fact]
    public void RejectsIncompleteTalkBoxLayouts()
    {
        BuildPack(layouts: 3);
        using var loader = new DialogueDataPackLoader(new DataPackReader(_root));
        Assert.Throws<CacheResourceException>(() => loader.LoadDialogueFrame());
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string File(DataPackWriteContext context, string relative, string content, bool register = true)
    {
        string path = context.GetImagePath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        if (register)
        {
            context.RegisterImage(relative);
        }

        return relative;
    }

    private static void Execute(DataPackWriteContext context, string sql)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void WriteTalks(DataPackWriteContext context, int textEntryCount, string? textJson)
    {
        Execute(context, "INSERT INTO dialogue_zones VALUES (200,512,1)");
        Execute(context, "INSERT INTO dialogue_actors VALUES (200,12,30,40,2,16)");
        Execute(context, "INSERT INTO dialogue_talks VALUES (200,12,0,5,0,'simple'),(200,12,1,6,1,'effect')");
        Execute(context, "INSERT INTO dialogue_codes VALUES (200,12,1,'condition',0,24580,1,'progress'),(200,12,1,'action',0,16,1,'flag_set'),(200,12,1,'action',1,36864,2,'start_event')");
        Execute(context, "INSERT INTO dialogue_talk_families VALUES (200,12,1,'start_event'),(200,12,1,'flag_set')");
        Execute(context, $"INSERT INTO text_files VALUES (1,'FSTALK01',{textEntryCount})");
        File(context, "text/talk01.json", textJson ?? """{"count":1,"entries":[[{"t":"g","c":65},{"t":"i","c":3},{"t":"c","cmd":2,"args":[7,8]}]]}""", register: false);
    }

    private static void WriteFonts(DataPackWriteContext context, int style2LineHeight)
    {
        foreach ((int style, int lineHeight) in new[] { (1, 14), (2, style2LineHeight), (3, 9) })
        {
            Execute(context, $"INSERT INTO font_styles VALUES ({style},{lineHeight},1,1)");
            Execute(context, $"INSERT INTO font_glyphs VALUES ({style},4,0,1,2,3,4,5,6,-1,-2,7)");
            Execute(context, $"INSERT INTO font_icons VALUES ({style},1,0,1,2,3,4,5,6,0,0,7)");
            for (int page = 0; page < 2; page++)
            {
                for (int palette = 0; palette < 8; palette++)
                {
                    File(context, $"font/font-style-{style}-palette-{palette}-page-{page}.png", "png");
                }
            }
        }

        File(context, "font/font-style-1-page-0.png", "png");
        File(context, "font/font-style-1-page-1.png", "png");
    }

    private static void WriteTalkBox(DataPackWriteContext context, int skipFrame, int layouts)
    {
        foreach (int frame in DialogueFrameNumbers)
        {
            if (frame == skipFrame)
            {
                continue;
            }

            string path = File(context, $"dialogue/frame/frame-{frame}.png", "png");
            Execute(context, $"INSERT INTO talk_box_frames VALUES ({frame},8,8,{(frame == 7 ? 4 : 1)},{(frame == 7 ? 0 : 1)},'{path}')");
        }

        string panel = File(context, "dialogue/frame/panel.png", "png");
        Execute(context, $"INSERT INTO talk_box_panel VALUES (222,238,255,1,69,11351,12,62,'{panel}')");
        int[] sprites = [3, 5, 5, 6];
        for (int layout = 0; layout < layouts; layout++)
        {
            Execute(context, $"INSERT INTO talk_box_layouts VALUES ({layout},{sprites[layout]},-16,-16,-26,-74,-10,-74,125,-74,-20,-71,-20,-58,125,-38)");
        }
    }

    private void BuildPack(int textEntryCount = 1, string? textJson = null, int style2LineHeight = 11, int skipFrame = -1, int layouts = 4)
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            context =>
            {
                WriteTalks(context, textEntryCount, textJson);
                WriteFonts(context, style2LineHeight);
                WriteTalkBox(context, skipFrame, layouts);
            });
    }
}
