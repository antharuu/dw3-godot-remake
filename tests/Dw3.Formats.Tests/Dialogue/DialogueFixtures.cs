using System.Buffers.Binary;
using System.Text;
using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;
using static Dw3.Formats.Tests.Mips.MipsAssembler;

namespace Dw3.Formats.Tests.Dialogue;

/// <summary>Synthetic layouts and buffers. No byte or value comes from a game disc.</summary>
internal static class DialogueFixtures
{
    public const uint ExeLoad = 0x80020000u;
    public const uint OverlayLoad = 0x800B0000u;

    public static readonly DialogueRomLayout Layout = new()
    {
        FileSectorsAddress = ExeLoad + 0x100,
        FileSectorCount = 16,
        LanguageAddress = 0x80050010,
        LanguageCopiesPerTextFile = 3,
        FieldStateAddress = 0x80091000,
        TextFileFieldOffset = 0x20,
        ActorListFieldOffset = 0x24,
        StageOverlayLoadAddress = OverlayLoad,
        ProgressRangesAddress = ExeLoad + 0x80,
        ProgressRangeCount = 3,
        SpecialConditionsAddress = ExeLoad + 0x60,
        ActorWidthsOffset = 4,
        ActorWidthCount = 6,
    };

    /// <summary>Setup code: text = [language] + selector, actor list = <paramref name="actorList"/> (0 for none).</summary>
    public static uint[] SetupCode(int selector, uint actorList)
    {
        var words = new List<uint>
        {
            Lui(V0, Layout.LanguageAddress), LwLow(V0, V0, Layout.LanguageAddress),
            Lui(S0, Layout.FieldStateAddress), AddiuLow(S0, S0, Layout.FieldStateAddress),
            Addiu(V0, V0, selector), Sw(V0, S0, Layout.TextFileFieldOffset),
        };
        if (actorList != 0)
        {
            words.AddRange([LuiForOri(T0, actorList), Ori(T0, T0, actorList), Sw(T0, S0, Layout.ActorListFieldOffset)]);
        }

        words.AddRange([Jr(Ra), Nop]);
        return [.. words];
    }

    public static byte[] Overlay(int size, params uint[] code)
    {
        byte[] overlay = new byte[size];
        Code(code).CopyTo(overlay, 0);
        return overlay;
    }

    public static void U32(byte[] buffer, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    public static void U16(byte[] buffer, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);

    public static void S16(byte[] buffer, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), value);

    /// <summary>Writes an 18-byte actor entry at overlay offset <paramref name="offset"/>.</summary>
    public static void Actor(byte[] overlay, int offset, short character, short x, short y, short direction, uint talks = 0)
    {
        U32(overlay, offset, 0);
        U32(overlay, offset + 4, talks);
        S16(overlay, offset + 8, character);
        S16(overlay, offset + 12, x);
        S16(overlay, offset + 14, y);
        S16(overlay, offset + 16, direction);
    }

    /// <summary>A PS-X EXE image of <paramref name="imageBytes"/> bytes loaded at <see cref="ExeLoad"/>.</summary>
    public static byte[] Executable(int imageBytes)
    {
        byte[] exe = new byte[PsxExecutable.HeaderBytes + imageBytes];
        Encoding.ASCII.GetBytes("PS-X EXE").CopyTo(exe, 0);
        U32(exe, 0x18, ExeLoad);
        U32(exe, 0x1C, (uint)imageBytes);
        return exe;
    }

    public static int ExeOffset(uint address) => PsxExecutable.HeaderBytes + (int)(address - ExeLoad);
}
