using System.Buffers.Binary;
using System.Text;

namespace Dw3.Extractor.Tests;

/// <summary>Builders for fabricated disc members: little-endian writes, PS-X EXE images and text tables.</summary>
internal static class Fabricate
{
    /// <summary>Load address of every fabricated executable image.</summary>
    internal const uint ExecutableLoadAddress = 0x80010000;

    /// <summary>RAM address of the FILE_SECTORS table the extractor reads from the executable.</summary>
    internal const uint FileSectorsAddress = 0x80044F6C;

    internal static void U32(byte[] buffer, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    internal static void U16(byte[] buffer, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);

    /// <summary>Returns a zero-filled PS-X EXE whose image spans the load address up to <paramref name="endAddress"/>.</summary>
    internal static byte[] Executable(uint endAddress)
    {
        int imageBytes = checked((int)(endAddress - ExecutableLoadAddress));
        var data = new byte[0x800 + imageBytes];
        Encoding.ASCII.GetBytes("PS-X EXE").CopyTo(data, 0);
        U32(data, 0x18, ExecutableLoadAddress);
        U32(data, 0x1C, (uint)imageBytes);
        return data;
    }

    /// <summary>Writes a 32-bit value at RAM address <paramref name="address"/> of a fabricated executable.</summary>
    internal static void ExecutableU32(byte[] executable, uint address, uint value) =>
        U32(executable, ExecutableOffset(address), value);

    /// <summary>Returns the file offset of RAM address <paramref name="address"/> in a fabricated executable.</summary>
    internal static int ExecutableOffset(uint address) => 0x800 + checked((int)(address - ExecutableLoadAddress));

    /// <summary>Records that engine file <paramref name="fileId"/> starts at <paramref name="lba"/>.</summary>
    internal static void FileSector(byte[] executable, int fileId, int lba) =>
        ExecutableU32(executable, FileSectorsAddress + (uint)(fileId * 4), (uint)lba);

    /// <summary>Builds a text table: entry count, one offset per entry, then each entry's bytes and a 0x00 terminator.</summary>
    internal static byte[] TextTable(params byte[][] entries)
    {
        int header = 4 + (entries.Length * 4);
        var data = new byte[header + entries.Sum(entry => entry.Length + 1)];
        U32(data, 0, (uint)entries.Length);
        int cursor = header;
        for (int index = 0; index < entries.Length; index++)
        {
            U32(data, 4 + (index * 4), (uint)cursor);
            entries[index].CopyTo(data, cursor);
            cursor += entries[index].Length + 1;
        }

        return data;
    }
}
