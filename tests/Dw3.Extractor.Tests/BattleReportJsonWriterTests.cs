using System.Text.Json;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Builds the battle-report sprite manifest from fabricated frame metadata and placeholder archives.
/// No game-derived bytes are used.
/// </summary>
public sealed class BattleReportJsonWriterTests
{
    [Fact]
    public void BuildWritesProvenanceFramesAnimationsAndArrowRows()
    {
        var frames = new List<(int Id, (string Path, int W, int H, int Ox, int Oy)[] Rows, (int Part, int Dx, int Dy)[] Parts)>
        {
            (3, [("report/frame-03-0.png", 16, 8, -2, 4), ("report/frame-03-1.png", 16, 8, -2, 4)], [(0, 1, 2), (1, -3, 0)]),
            (7, [("report/frame-07-0.png", 4, 4, 0, 0)], []),
        };
        var arrowParts = new List<FieldFramePartMeta> { new(1, 2, 3, 4, 5, 6, true, 0) };
        var arrowRows = new List<(string Path, int W, int H, int Ox, int Oy, IReadOnlyList<FieldFramePartMeta> Parts)>
        {
            ("report/arrow-0.png", 8, 6, 1, -1, arrowParts),
            ("report/arrow-1.png", 8, 6, 1, -1, [new FieldFramePartMeta(9, 9, 2, 2, 0, 0, false, 1)]),
        };
        var animations = new int[BattleReportDecoder.AnimationPartners, BattleReportDecoder.AnimationFrames];
        for (int partner = 0; partner < BattleReportDecoder.AnimationPartners; partner++)
        {
            for (int frame = 0; frame < BattleReportDecoder.AnimationFrames; frame++)
            {
                animations[partner, frame] = (partner * 10) + frame;
            }
        }

        string text = BattleReportJsonWriter.Build(frames, arrowRows, arrowParts, animations, [1, 2, 3], [4], []);

        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        Assert.Equal("m6-battle-report-sprites-v1", root.GetProperty("schema").GetString());
        Assert.Equal(2, root.GetProperty("frameCount").GetInt32());
        Assert.Equal([3, 7], root.GetProperty("frameIds").EnumerateArray().Select(id => id.GetInt32()));

        JsonElement source = root.GetProperty("source");
        Assert.Equal(3, source.GetProperty("sheet").GetProperty("bytes").GetInt32());
        Assert.Equal(
            "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81",
            source.GetProperty("sheet").GetProperty("sha256").GetString());
        Assert.Equal(0, source.GetProperty("overlay").GetProperty("bytes").GetInt32());
        Assert.Equal(BattleReportDecoder.OverlayDiscPath, source.GetProperty("overlay").GetProperty("path").GetString());
        Assert.Equal(BattleReportDecoder.ArrowRows, source.GetProperty("arrow").GetProperty("clutRows").GetInt32());

        JsonElement frame3 = root.GetProperty("frames").GetProperty("3");
        Assert.Equal("report/frame-03-0.png", frame3.GetProperty("path").GetString());
        Assert.Equal(-2, frame3.GetProperty("offsetX").GetInt32());
        Assert.Equal(-3, frame3.GetProperty("parts")[1].GetProperty("dx").GetInt32());
        Assert.Equal("report/frame-03-1.png", frame3.GetProperty("clutRows")[1].GetProperty("path").GetString());
        Assert.Equal(0, root.GetProperty("frames").GetProperty("7").GetProperty("parts").GetArrayLength());

        JsonElement animationRows = root.GetProperty("animations");
        Assert.Equal(BattleReportDecoder.AnimationPartners, animationRows.GetArrayLength());
        Assert.Equal(76, animationRows[7][6].GetInt32());

        JsonElement arrow = root.GetProperty("arrow");
        Assert.Equal(BattleReportDecoder.ArrowFrameId, arrow.GetProperty("frameId").GetInt32());
        Assert.Equal("report/arrow-0.png", arrow.GetProperty("path").GetString());
        Assert.Equal(6, arrow.GetProperty("parts")[0].GetProperty("dy").GetInt32());
        Assert.Equal(9, arrow.GetProperty("clutRows")[1].GetProperty("parts")[0].GetProperty("u").GetInt32());
    }
}
