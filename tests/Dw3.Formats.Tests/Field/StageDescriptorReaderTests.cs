using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class StageDescriptorReaderTests
{
    // Register numbers used by the fabricated instruction streams.
    private const int V0 = 2;

    // Register numbers used by the fabricated instruction streams.
    private const int V1 = 3;

    // Register numbers used by the fabricated instruction streams.
    private const int S1 = 17;

    // Register numbers used by the fabricated instruction streams.
    private const int Ra = 31;

    [Fact]
    public void RecoversObjectTablePointerAndSheetEntryFromDescriptorStores()
    {
        uint[] code =
        [
            Lui(V1, 0x0123), Ori(V1, V1, 1),
            Lui(V0, 0x800A), Addiu(S1, V0, unchecked((short)0x9D80)),
            Lui(V0, 0x800A), Addiu(V0, V0, 0x5E40),
            Sw(V0, S1, 0x10),
            Sw(V1, S1, 0x0C),
            Jr(Ra), 0,
        ];

        StageDescriptor descriptor = StageDescriptorReader.Read(Assemble(code));

        Assert.Equal(0x800A5E40u, descriptor.ObjectsAddress);
        Assert.Equal(1, descriptor.SheetEntry);
    }

    [Fact]
    public void ReadsEntryZeroWhenSheetWordCarriesOnlyTheFileNumber()
    {
        uint[] code =
        [
            Lui(V0, 0x800A), Addiu(S1, V0, unchecked((short)0x9D80)),
            Lui(V0, 0x0456), Sw(V0, S1, 0x0C),
            Lui(V0, 0x800A), Addiu(V0, V0, 0x5E40), Sw(V0, S1, 0x10),
            Jr(Ra), 0,
        ];

        Assert.Equal(0, StageDescriptorReader.Read(Assemble(code)).SheetEntry);
    }

    [Fact]
    public void RejectsOverlayWithoutObjectTableStore()
    {
        uint[] code = [Lui(V0, 0x800A), Addiu(S1, V0, unchecked((short)0x9D80)), Lui(V0, 0x0456), Sw(V0, S1, 0x0C), Jr(Ra), 0];

        FormatException error = Assert.Throws<FormatException>(() => StageDescriptorReader.Read(Assemble(code)));
        Assert.Contains("object table", error.Message);
    }

    [Fact]
    public void RejectsConflictingObjectTableStores()
    {
        uint[] code =
        [
            Lui(V0, 0x800A), Addiu(S1, V0, unchecked((short)0x9D80)),
            Lui(V1, 0x0456), Sw(V1, S1, 0x0C),
            Lui(V0, 0x800A), Addiu(V0, V0, 0x5E40), Sw(V0, S1, 0x10),
            Lui(V0, 0x800A), Addiu(V0, V0, 0x5E80), Sw(V0, S1, 0x10),
            Jr(Ra), 0,
        ];

        Assert.Throws<FormatException>(() => StageDescriptorReader.Read(Assemble(code)));
    }

    [Fact]
    public void ACallClobbersTemporaryRegistersSoTheStoredValueIsNotConstant()
    {
        uint[] code =
        [
            Lui(V0, 0x800A), Addiu(S1, V0, unchecked((short)0x9D80)),
            Lui(V1, 0x0456), Sw(V1, S1, 0x0C),
            Lui(V0, 0x800A), Addiu(V0, V0, 0x5E40),
            Jal(0x100), 0,
            Sw(V0, S1, 0x10),
            Jr(Ra), 0,
        ];

        FormatException error = Assert.Throws<FormatException>(() => StageDescriptorReader.Read(Assemble(code)));
        Assert.Contains("not a constant", error.Message);
    }

    [Fact]
    public void RejectsObjectPointerOutsideTheOverlayImage()
    {
        uint[] code =
        [
            Lui(V0, 0x800A), Addiu(S1, V0, unchecked((short)0x9D80)),
            Lui(V1, 0x0456), Sw(V1, S1, 0x0C),
            Lui(V0, 0x8010), Sw(V0, S1, 0x10),
            Jr(Ra), 0,
        ];

        Assert.Throws<FormatException>(() => StageDescriptorReader.Read(Assemble(code)));
    }

    private static byte[] Assemble(uint[] words)
    {
        byte[] bytes = new byte[(words.Length * 4) + 0x40];
        for (int index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }

        return bytes;
    }

    private static uint Lui(int rt, ushort value) => (15u << 26) | ((uint)rt << 16) | value;

    private static uint Ori(int rt, int rs, ushort value) => (13u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | value;

    private static uint Addiu(int rt, int rs, short value) => (9u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (ushort)value;

    private static uint Sw(int rt, int baseRegister, short offset) => (43u << 26) | ((uint)baseRegister << 21) | ((uint)rt << 16) | (ushort)offset;

    private static uint Jr(int rs) => ((uint)rs << 21) | 8;

    private static uint Jal(uint target) => (3u << 26) | (target & 0x03FFFFFF);
}
