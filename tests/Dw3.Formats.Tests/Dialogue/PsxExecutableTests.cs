using Dw3.Formats.Binary;
using Xunit;

namespace Dw3.Formats.Tests.Dialogue;

public sealed class PsxExecutableTests
{
    [Fact]
    public void MapsImageAddressesThroughTheHeaderLoadAddress()
    {
        PsxExecutable exe = PsxExecutable.Parse(DialogueFixtures.Executable(0x200));

        Assert.Equal(DialogueFixtures.ExeLoad, exe.LoadAddress);
        Assert.Equal(0x810, exe.OffsetOf(DialogueFixtures.ExeLoad + 0x10, 4));
    }

    [Theory]
    [InlineData(0x8001FFFCu, 4)]
    [InlineData(0x800201FEu, 4)]
    [InlineData(0x80020200u, 1)]
    public void RejectsRangesOutsideTheImage(uint address, int size)
    {
        PsxExecutable exe = PsxExecutable.Parse(DialogueFixtures.Executable(0x200));

        Assert.Throws<FormatException>(() => exe.OffsetOf(address, size));
    }

    [Fact]
    public void RejectsMissingMagic()
    {
        byte[] exe = DialogueFixtures.Executable(0x10);
        exe[0] = (byte)'X';

        Assert.Throws<FormatException>(() => PsxExecutable.Parse(exe));
    }

    [Fact]
    public void RejectsImageLargerThanTheFile()
    {
        byte[] exe = DialogueFixtures.Executable(0x10);
        DialogueFixtures.U32(exe, 0x1C, 0x20);

        Assert.Throws<FormatException>(() => PsxExecutable.Parse(exe));
    }
}
