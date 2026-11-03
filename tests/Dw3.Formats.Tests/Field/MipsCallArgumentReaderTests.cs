using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class MipsCallArgumentReaderTests
{
    [Fact]
    public void RecoversRequiredArgumentRegistersAtDirectCall()
    {
        uint[] words =
        [
            Addiu(4, 0, 1),
            Addiu(5, 0, 2),
            Addiu(6, 0, 3),
            Jal(0x001234),
            0u,
        ];

        MipsCallArguments arguments = MipsCallArgumentReader.RecoverDirectCall(ToBytes(words), 0x001234);

        Assert.Equal(1u, arguments.A0);
        Assert.Equal(2u, arguments.A1);
        Assert.Equal(3u, arguments.A2);
        Assert.Null(arguments.A3);
    }

    [Fact]
    public void RejectsControlFlowBeforeRequestedCall()
    {
        uint[] words =
        [
            Addiu(4, 0, 1),
            0x10000001u,
            Jal(0x001234),
            0u,
        ];

        FormatException error = Assert.Throws<FormatException>(() => MipsCallArgumentReader.RecoverDirectCall(ToBytes(words), 0x001234));

        Assert.Contains("control flow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ToBytes(uint[] words)
    {
        var bytes = new byte[words.Length * sizeof(uint)];
        for (int index = 0; index < words.Length; index++)
        {
            BitConverter.GetBytes(words[index]).CopyTo(bytes, index * sizeof(uint));
        }

        return bytes;
    }

    private static uint Addiu(int target, int source, short immediate) =>
        0x24000000u | ((uint)source << 21) | ((uint)target << 16) | (ushort)immediate;

    private static uint Jal(int targetWordAddress) => 0x0C000000u | (uint)targetWordAddress;
}
