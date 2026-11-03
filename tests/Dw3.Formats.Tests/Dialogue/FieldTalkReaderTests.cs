using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Dialogue;

public sealed class FieldTalkReaderTests
{
    private const uint Load = DialogueFixtures.OverlayLoad;

    [Fact]
    public void ReadsRecordsAndTheirPairListsUntilTheZeroRecord()
    {
        byte[] overlay = new byte[0x100];
        DialogueFixtures.U32(overlay, 0x10, Load + 0x80);
        DialogueFixtures.U32(overlay, 0x14, Load + 0x90);
        DialogueFixtures.U32(overlay, 0x18, unchecked((uint)-3));
        DialogueFixtures.U32(overlay, 0x1C, 0);
        DialogueFixtures.U32(overlay, 0x20, 0);
        DialogueFixtures.U32(overlay, 0x24, 12);
        DialogueFixtures.U16(overlay, 0x80, 0x6004);
        DialogueFixtures.U16(overlay, 0x82, 1);
        DialogueFixtures.U16(overlay, 0x84, 0xFFFF);
        DialogueFixtures.U16(overlay, 0x90, 0x0105);
        DialogueFixtures.U16(overlay, 0x92, 2);
        DialogueFixtures.U16(overlay, 0x94, 0xFFFF);

        IReadOnlyList<FieldTalk> talks = FieldTalkReader.Read(overlay, Load + 0x10, Load);

        Assert.Equal(2, talks.Count);
        Assert.Equal([new NpcCondition(0x6004, 1)], talks[0].Conditions);
        Assert.Equal([new NpcCondition(0x0105, 2)], talks[0].Actions);
        Assert.Equal(-3, talks[0].Entry);
        Assert.Empty(talks[1].Conditions);
        Assert.Equal(12, talks[1].Entry);
    }

    [Fact]
    public void NullTableIsAnActorWithoutTalks() => Assert.Empty(FieldTalkReader.Read(new byte[16], 0, Load));

    [Fact]
    public void RejectsTableWithoutSentinelBeforeTheOverlayEnd()
    {
        byte[] overlay = new byte[0x20];
        DialogueFixtures.U32(overlay, 0x18, 7);

        Assert.Throws<FormatException>(() => FieldTalkReader.Read(overlay, Load + 0x10, Load));
    }

    [Fact]
    public void RejectsTablePointerOutsideTheOverlay()
    {
        Assert.Throws<FormatException>(() => FieldTalkReader.Read(new byte[0x20], Load - 4, Load));
        Assert.Throws<FormatException>(() => FieldTalkReader.Read(new byte[0x20], Load + 0x40, Load));
    }

    [Fact]
    public void RejectsPairListWithoutTerminator()
    {
        byte[] overlay = new byte[0x40];
        DialogueFixtures.U32(overlay, 0, Load + 0x38);
        DialogueFixtures.U32(overlay, 8, 1);
        overlay.AsSpan(0x38).Fill(0x11);

        Assert.Throws<FormatException>(() => FieldTalkReader.Read(overlay, Load, Load));
    }
}
