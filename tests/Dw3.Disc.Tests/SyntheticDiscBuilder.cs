using System.Text;

namespace Dw3.Disc.Tests;

/// <summary>
/// Builds a minimal Mode-2 ISO9660 BIN image for unit tests.
/// All content is fabricated; no game-derived bytes are used.
/// Structure:
///   Sectors 0-15  : empty padding
///   Sector 16     : Primary Volume Descriptor (volume ID "SYNTH")
///   Sector 17     : L-type Path Table
///   Sector 18     : Root directory
///   Sector 19     : SUBDIR directory
///   Sector 20     : HELLO.TXT content
///   Sector 21     : SUBDIR/DATA.BIN content
/// </summary>
internal static class SyntheticDiscBuilder
{
    private const int Sec = Iso9660Reader.SectorBytes;
    private const int UDO = Iso9660Reader.UserDataOffset;
    private const int UDB = Iso9660Reader.UserDataBytes;

    private const int PvdLba = 16;
    private const int PTabLba = 17;
    private const int RootDirLba = 18;
    private const int SubDirLba = 19;
    private const int HelloLba = 20;
    private const int DataLba = 21;
    private const int TotalSecs = 22;

    public static readonly byte[] HelloContent = Encoding.ASCII.GetBytes("hello world");
    public static readonly byte[] DataContent = [0x01, 0x02, 0x03, 0x04];

    public static byte[] Build()
    {
        byte[] bin = new byte[TotalSecs * Sec];

        // ── Directory records ──────────────────────────────────────────────

        // Root directory.  Pass the correct sizes after we know them.
        byte[] subDir = MakeDirectory(SubDirLba, RootDirLba, 0,
            [MakeFileRecord("DATA.BIN;1", DataLba, DataContent.Length)]);
        // Now we know subDir.Length, so build root with correct subdir data length.
        byte[] rootDir = MakeDirectory(RootDirLba, RootDirLba, 0,
            [
                MakeFileRecord("HELLO.TXT;1", HelloLba, HelloContent.Length),
                MakeDirRecord("SUBDIR",       SubDirLba, subDir.Length),
            ]);
        // Patch dot-entry sizes now that we know the actual lengths.
        PatchU32(rootDir, 10, rootDir.Length);
        PatchU32(subDir, 10, subDir.Length);

        // ── Path table ─────────────────────────────────────────────────────
        byte[] pathTable = MakePathTable();

        // ── PVD ────────────────────────────────────────────────────────────
        byte[] pvd = MakePvd(PTabLba, pathTable.Length, RootDirLba, rootDir.Length);

        // ── Write sectors ──────────────────────────────────────────────────
        Write(bin, PvdLba, pvd);
        Write(bin, PTabLba, pathTable);
        Write(bin, RootDirLba, rootDir);
        Write(bin, SubDirLba, subDir);
        Write(bin, HelloLba, HelloContent);
        Write(bin, DataLba, DataContent);
        return bin;
    }

    // ── Directory builders ─────────────────────────────────────────────────

    /// <summary>
    /// Builds a directory blob containing dot, dotdot, plus <paramref name="extra"/> records.
    /// dotSize / dotDotSize are set to <paramref name="ownSize"/> initially
    /// as initial values; callers patch offset 10 afterwards when real sizes are known.
    /// </summary>
    private static byte[] MakeDirectory(int ownLba, int parentLba, int ownSize,
        IReadOnlyList<byte[]> extra)
    {
        byte[] dot = MakeSpecialDirRec(0x00, ownLba, ownSize);
        byte[] dotdot = MakeSpecialDirRec(0x01, parentLba, 0);
        int total = dot.Length + dotdot.Length + extra.Sum(r => r.Length);
        byte[] buf = new byte[total];
        int pos = 0;
        foreach (byte[] r in new[] { dot, dotdot }.Concat(extra))
        {
            r.CopyTo(buf, pos);
            pos += r.Length;
        }

        return buf;
    }

    /// <summary>Creates the dot (id=0x00) or dotdot (id=0x01) special directory entry.</summary>
    private static byte[] MakeSpecialDirRec(byte nameId, int lba, int size)
    {
        byte[] r = NewRecord(nameLen: 1, flags: 0x02);
        r[33] = nameId;
        PatchU32(r, 2, lba);
        PatchU32(r, 10, size);
        return r;
    }

    private static byte[] MakeFileRecord(string name, int lba, int size)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] r = NewRecord(nameLen: nameBytes.Length, flags: 0x00);
        nameBytes.CopyTo(r, 33);
        PatchU32(r, 2, lba);
        PatchU32(r, 10, size);
        return r;
    }

    private static byte[] MakeDirRecord(string name, int lba, int size)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] r = NewRecord(nameLen: nameBytes.Length, flags: 0x02);
        nameBytes.CopyTo(r, 33);
        PatchU32(r, 2, lba);
        PatchU32(r, 10, size);
        return r;
    }

    /// <summary>Allocates a zeroed directory record of the correct padded length.</summary>
    private static byte[] NewRecord(int nameLen, byte flags)
    {
        int raw = 33 + nameLen;
        int len = raw % 2 == 0 ? raw : raw + 1;
        byte[] r = new byte[len];
        r[0] = (byte)len;
        r[25] = flags;
        r[32] = (byte)nameLen;
        r[28] = 1; // Volume Sequence Number LE = 1
        return r;
    }

    // ── Path table ─────────────────────────────────────────────────────────

    private static byte[] MakePathTable()
    {
        var buf = new List<byte>
        {
            // Entry 1: root directory.  Name = single byte 0x01; parent = 1 (self).
            1,                       // name length
            0, // ext attr record length
        };
        buf.AddRange(LE32(RootDirLba));
        buf.AddRange(LE16(1));            // parent = 1
        buf.Add(0x01);                    // name
        buf.Add(0);                       // padding (name length odd → pad to even)

        // Entry 2: SUBDIR.  Parent = 1 (root).
        byte[] subName = "SUBDIR"u8.ToArray();
        buf.Add((byte)subName.Length);    // 6
        buf.Add(0);
        buf.AddRange(LE32(SubDirLba));
        buf.AddRange(LE16(1));            // parent = 1
        buf.AddRange(subName);            // 6 bytes, even → no padding

        return [.. buf];
    }

    // ── PVD ────────────────────────────────────────────────────────────────

    private static byte[] MakePvd(int pathTabLba, int pathTabSize, int rootDirLba, int rootDirSize)
    {
        byte[] pvd = new byte[UDB];
        pvd[0] = 1;
        pvd[1] = (byte)'C';
        pvd[2] = (byte)'D';
        pvd[3] = (byte)'0';
        pvd[4] = (byte)'0';
        pvd[5] = (byte)'1';
        pvd[6] = 1;
        // Volume Identifier at offset 40, space-padded to 32 bytes.
        Encoding.ASCII.GetBytes("SYNTH".PadRight(32)).CopyTo(pvd, 40);
        // Path table size (LE u32) at offset 132.
        LE32(pathTabSize).CopyTo(pvd, 132);
        // L-type path table LBA (LE u32) at offset 140.
        LE32(pathTabLba).CopyTo(pvd, 140);
        // Root directory record at offset 156 (the 34-byte dot record for the root dir).
        byte[] rootRec = MakeSpecialDirRec(0x00, rootDirLba, rootDirSize);
        rootRec.CopyTo(pvd, 156);
        return pvd;
    }

    // ── Sector write ───────────────────────────────────────────────────────

    private static void Write(byte[] bin, int lba, byte[] userData)
    {
        int off = lba * Sec + UDO;
        int copy = Math.Min(userData.Length, UDB);
        Array.Copy(userData, 0, bin, off, copy);
    }

    // ── Byte helpers ───────────────────────────────────────────────────────

    private static void PatchU32(byte[] buf, int offset, int value) =>
        LE32(value).CopyTo(buf, offset);

    private static byte[] LE32(int v) =>
    [
        (byte)(v & 0xFF),
        (byte)((v >> 8) & 0xFF),
        (byte)((v >> 16) & 0xFF),
        (byte)((v >> 24) & 0xFF),
    ];

    private static byte[] LE16(int v) =>
    [
        (byte)(v & 0xFF),
        (byte)((v >> 8) & 0xFF),
    ];
}
