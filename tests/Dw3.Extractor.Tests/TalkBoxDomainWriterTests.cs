using Dw3.Data;
using Dw3.Extractor;
using Dw3.Formats.Dialogue;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class TalkBoxDomainWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-talkbox-{Guid.NewGuid():N}");

    [Fact]
    public void WritesFramesPanelAndLayouts()
    {
        var domain = new TalkBoxDomain(
            [new TalkBoxFrameImage(3, 1, 1, 1, true, [1, 2, 3, 255]), new TalkBoxFrameImage(7, 1, 1, 4, false, [4, 5, 6, 255])],
            new TalkBoxPanelImage(1, 2, 9, 8, 7, 0x45, 0x2C57, [9, 8, 7, 255, 1, 1, 1, 128]),
            [Layout(3), Layout(5), Layout(4), Layout(6)]);

        new DataPackWriter(_root).Write(Metadata(), context => TalkBoxDomainWriter.Write(context, domain));

        string data = Path.Combine(_root, "data");
        Assert.True(File.Exists(Path.Combine(data, "dialogue", "frame", "frame-3.png")));
        Assert.True(File.Exists(Path.Combine(data, "dialogue", "frame", "frame-7.png")));
        Assert.True(File.Exists(Path.Combine(data, "dialogue", "frame", "panel.png")));
        using SqliteConnection connection = new DataPackReader(_root).OpenDatabase();
        Assert.Equal("4|1|0|dialogue/frame/frame-7.png", Row(connection, "SELECT clut_frames, width, semi, image_path FROM talk_box_frames WHERE frame_id = 7"));
        Assert.Equal("1|1|dialogue/frame/frame-3.png", Row(connection, "SELECT clut_frames, semi, image_path FROM talk_box_frames WHERE frame_id = 3"));
        Assert.Equal("9|8|7|1|69|11351|1|2|dialogue/frame/panel.png", Row(connection, "SELECT red, green, blue, semi, texture_page, clut, width, height, image_path FROM talk_box_panel"));
        Assert.Equal("4|-1|-2|-3|-4|-5|-6|-7|-8|-9|-10|-11|-12|-13|-14", Row(connection, "SELECT sprite, sprite_x, sprite_y, sprite0_x, sprite0_y, panel_x, panel_y, sprite2_x, sprite2_y, name_x, name_y, text_x, text_y, arrow_x, arrow_y FROM talk_box_layouts WHERE layout = 2"));
    }

    [Fact]
    public void RejectsIncompleteLayoutTable()
    {
        var domain = new TalkBoxDomain([new TalkBoxFrameImage(3, 1, 1, 1, true, [1, 2, 3, 255])],
            new TalkBoxPanelImage(1, 1, 9, 8, 7, 0x45, 0x2C57, [9, 8, 7, 255]), [Layout(3)]);
        Assert.ThrowsAny<Exception>(() => new DataPackWriter(_root).Write(Metadata(), context => TalkBoxDomainWriter.Write(context, domain)));
    }

    [Fact]
    public void RejectsMissingFrames()
    {
        var domain = new TalkBoxDomain([], new TalkBoxPanelImage(1, 1, 9, 8, 7, 0x45, 0x2C57, [9, 8, 7, 255]),
            [Layout(3), Layout(5), Layout(4), Layout(6)]);
        Assert.ThrowsAny<Exception>(() => new DataPackWriter(_root).Write(Metadata(), context => TalkBoxDomainWriter.Write(context, domain)));
    }

    public void Dispose()
    {
        // Release pooled connections before deletion; on Windows, the pool keeps
        // the file handle open past Dispose(), preventing Directory.Delete.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static TalkBoxLayoutRecord Layout(int sprite) =>
        new(sprite, -1, -2, -3, -4, -5, -6, -7, -8, -9, -10, -11, -12, -13, -14);

    private static string Row(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read());
        string value = string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(reader.Read());
        return value;
    }

    private static DataPackMetadata Metadata() => new("test", "0123456789012345678901234567890123456789", "synthetic", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
}
