using System.Buffers.Binary;

namespace Dw3.Formats.Tests.Mips;

/// <summary>Encodes the small R3000 instruction subset used to fabricate test code streams.</summary>
internal static class MipsAssembler
{
    public const int Zero = 0;

    public const int At = 1;

    public const int V0 = 2;

    public const int V1 = 3;

    public const int A0 = 4;

    public const int A1 = 5;

    public const int T0 = 8;

    public const int T1 = 9;

    public const int S0 = 16;

    public const int S1 = 17;

    public const int Sp = 29;

    public const int Ra = 31;

    public const uint Nop = 0;

    /// <summary>High half for an <c>addiu</c>/<c>lw</c> low half (sign-adjusted).</summary>
    public static uint Lui(int rt, uint value) => I(0x0F, 0, rt, (value + 0x8000u) >> 16);

    /// <summary>High half for an <c>ori</c> low half (not sign-adjusted).</summary>
    public static uint LuiForOri(int rt, uint value) => I(0x0F, 0, rt, value >> 16);

    public static uint Addiu(int rt, int rs, int immediate) => I(0x09, rs, rt, (uint)immediate & 0xFFFF);

    public static uint AddiuLow(int rt, int rs, uint value) => I(0x09, rs, rt, value & 0xFFFF);

    public static uint Ori(int rt, int rs, uint value) => I(0x0D, rs, rt, value & 0xFFFF);

    public static uint Lw(int rt, int rs, int offset) => I(0x23, rs, rt, (uint)offset & 0xFFFF);

    public static uint LwLow(int rt, int rs, uint value) => I(0x23, rs, rt, value & 0xFFFF);

    public static uint Sw(int rt, int rs, int offset) => I(0x2B, rs, rt, (uint)offset & 0xFFFF);

    public static uint Lbu(int rt, int rs, int offset) => I(0x24, rs, rt, (uint)offset & 0xFFFF);

    public static uint Jal(uint address) => (0x03u << 26) | ((address >> 2) & 0x03FFFFFF);

    public static uint J(uint address) => (0x02u << 26) | ((address >> 2) & 0x03FFFFFF);

    public static uint Jr(int rs) => R(rs, 0, 0, 0x08);

    public static uint Addu(int rd, int rs, int rt) => R(rs, rt, rd, 0x21);

    public static uint Or(int rd, int rs, int rt) => R(rs, rt, rd, 0x25);

    public static uint Subu(int rd, int rs, int rt) => R(rs, rt, rd, 0x23);

    public static uint Sll(int rd, int rt, int shift) => ((uint)rt << 16) | ((uint)rd << 11) | ((uint)shift << 6);

    public static byte[] Code(params uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }

        return bytes;
    }

    private static uint I(uint opcode, int rs, int rt, uint immediate) =>
        (opcode << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (immediate & 0xFFFF);

    private static uint R(int rs, int rt, int rd, uint function) =>
        ((uint)rs << 21) | ((uint)rt << 16) | ((uint)rd << 11) | function;
}
