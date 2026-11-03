using Dw3.Formats.Dialogue;
using Dw3.Formats.Mips;
using Xunit;
using static Dw3.Formats.Tests.Mips.MipsAssembler;

namespace Dw3.Formats.Tests.Dialogue;

public sealed class StageDialogueScannerTests
{
    private const uint Load = DialogueFixtures.OverlayLoad;

    [Fact]
    public void RecoversTextSelectorAndValidatedActorList()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(0x2A, Load + 0x100));
        DialogueFixtures.U32(overlay, 0x100, Load + 0x140);
        DialogueFixtures.U32(overlay, 0x104, 0);
        DialogueFixtures.Actor(overlay, 0x140, character: 2, x: 10, y: 20, direction: 3);

        StageDialogueSetup setup = StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout);

        Assert.Equal(0x2A, setup.TextSelector);
        Assert.NotNull(setup.ActorList);
        Assert.Equal(Load + 0x100, setup.ActorList.Address);
        Assert.Equal([Load + 0x140], setup.ActorList.Actors);
    }

    [Fact]
    public void StageWithoutActorStoreHasNoActorList()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x80, DialogueFixtures.SetupCode(0x2A, 0));

        Assert.Null(StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout).ActorList);
    }

    [Fact]
    public void MissingTextStoreIsADecodeError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x80, Lui(S0, DialogueFixtures.Layout.FieldStateAddress), Jr(Ra), Nop);

        Assert.Throws<MipsDecodeException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }

    [Fact]
    public void ConstantTextStoreIsNotALanguageRelativeSelector()
    {
        DialogueRomLayout layout = DialogueFixtures.Layout;
        byte[] overlay = DialogueFixtures.Overlay(
            0x80,
            Lui(S0, layout.FieldStateAddress),
            AddiuLow(S0, S0, layout.FieldStateAddress),
            Addiu(V0, Zero, 0x2A),
            Sw(V0, S0, layout.TextFileFieldOffset));

        Assert.Throws<MipsDecodeException>(() => StageDialogueScanner.Scan(overlay, layout));
    }

    [Fact]
    public void ConflictingSelectorsAreADecodeError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x100, [.. DialogueFixtures.SetupCode(0x2A, 0), .. DialogueFixtures.SetupCode(0x31, 0)]);

        Assert.Throws<MipsDecodeException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }

    [Fact]
    public void RepeatedIdenticalSelectorIsAccepted()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x100, [.. DialogueFixtures.SetupCode(0x2A, 0), .. DialogueFixtures.SetupCode(0x2A, 0)]);

        Assert.Equal(0x2A, StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout).TextSelector);
    }

    [Fact]
    public void ActorStoreToAnInvalidListIsAnError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(0x2A, Load + 0x100));
        DialogueFixtures.U32(overlay, 0x100, Load + 0x140);
        DialogueFixtures.Actor(overlay, 0x140, character: 2, x: 0, y: 0, direction: 9);

        Assert.Throws<FormatException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }

    [Fact]
    public void ActorStoreBelowTheOverlayIsAnError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(0x2A, Load - 0x100));

        Assert.Throws<FormatException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }
}
