using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class FieldcomBehaviorDecoderTests
{
    [Fact]
    public void RecoversFiveCueInnerRowsAndFourIconLoopsFromOverlayBytes()
    {
        byte[] overlay =
        [
            0x10, 22, 0x11, 22, 0xFF, 0, 0, 0, 0,
            0x0E, 22, 0x0F, 22, 0xFF, 0, 0, 0, 0,
            0x0C, 22, 0x0D, 22, 0xFF, 0, 0, 0, 0,
            0x12, 22, 0x13, 22, 0xFF, 0, 0, 0, 0,
            0x45, 22, 0x46, 22, 0xFF, 0, 0, 0, 0,
            0x23, 8, 0x24, 8, 0x25, 8, 0x26, 8, 0xFF, 0,
            0x23, 3, 0x27, 4, 0xFF, 1,
            0x29, 6, 0x2E, 3, 0xFF, 0,
            0x23, 3, 0x27, 4, 0x2D, 4, 0xFF, 2,
        ];

        IReadOnlyList<FieldcomFrameSequence> inner = FieldcomBehaviorDecoder.DecodeCueInnerSequences(overlay);
        IReadOnlyList<FieldcomAnimation> icons = FieldcomBehaviorDecoder.DecodeIconAnimations(overlay, Enumerable.Range(0x23, 15).ToHashSet());

        Assert.Equal(5, inner.Count);
        Assert.Equal(new[] { 0x0C, 0x0E, 0x10, 0x12, 0x45 }, inner.Select(sequence => sequence.Steps[0].Frame).Order());
        Assert.Equal(4, icons.Count);
        Assert.Equal(0, icons[0].LoopTarget);
        Assert.Equal(2, icons[3].LoopTarget);
    }

    [Fact]
    public void RejectsCueSequenceWithoutTerminator()
    {
        FormatException error = Assert.Throws<FormatException>(() => FieldcomBehaviorDecoder.DecodeCueInnerSequences([0x10, 22, 0x11, 22]));

        Assert.Contains("cue", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsIconLoopThatTargetsTheSentinel()
    {
        FormatException error = Assert.Throws<FormatException>(() => FieldcomBehaviorDecoder.DecodeIconAnimations([0x23, 8, 0xFF, 1], new HashSet<int> { 0x23 }));

        Assert.Contains("icon", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
