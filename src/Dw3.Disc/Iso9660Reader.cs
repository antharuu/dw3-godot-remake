using System.Text;

namespace Dw3.Disc;

/// <summary>
/// Read-only ISO9660 reader for Mode-2 BIN images (2352-byte raw sectors).
/// Only the L-type (little-endian) path table and directory records are used.
/// </summary>
public static class Iso9660Reader
{
    /// <summary>Raw bytes per sector in the BIN image.</summary>
    public const int SectorBytes = 2352;

    /// <summary>Byte offset from the start of a sector to user data (after sync + header + sub-header).</summary>
    public const int UserDataOffset = 24;

    /// <summary>User-data bytes per sector (ISO9660 standard).</summary>
    public const int UserDataBytes = 2048;

    private const int PvdLba = 16;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the volume identifier string from the Primary Volume Descriptor.
    /// Returns the value trimmed of trailing spaces.
    /// </summary>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> if the image is truncated or malformed.
    /// </exception>
    public static string ReadVolumeId(Stream source)
    {
        byte[] pvd = ReadSector(source, PvdLba);
        ValidatePvd(pvd);
        return Encoding.ASCII.GetString(pvd, 40, 32).TrimEnd(' ');
    }

    /// <summary>
    /// Returns every file in the disc image in path-table traversal order.
    /// Directories and special dot entries are excluded.
    /// </summary>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> on structural problems.
    /// </exception>
    public static IReadOnlyList<DiscFileEntry> ListFiles(Stream source)
    {
        byte[] pvd = ReadSector(source, PvdLba);
        ValidatePvd(pvd);

        List<(string Name, int Lba, int Parent)> directories = ReadPathTable(source, pvd);
        var results = new List<DiscFileEntry>();

        for (int dirIdx = 0; dirIdx < directories.Count; dirIdx++)
        {
            ReadDirectoryFiles(source, directories, dirIdx, results);
        }

        return results;
    }

    /// <summary>
    /// Reads the raw user-data bytes of a file identified by its extent.
    /// </summary>
    public static byte[] ReadFileContent(Stream source, int lba, int size)
    {
        if (size <= 0)
        {
            return [];
        }

        return ReadContiguous(source, lba, size);
    }

    // ── Internal sector / structure helpers ────────────────────────────────

    /// <summary>
    /// Reads the 2048-byte user-data region of sector <paramref name="lba"/>
    /// from a 2352-byte-per-sector raw BIN stream.
    /// </summary>
    internal static byte[] ReadSector(Stream source, int lba)
    {
        if (lba < 0)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"Negative ISO9660 sector LBA: {lba}.");
        }

        long position = (long)lba * SectorBytes + UserDataOffset;
        if (position < 0 || position + UserDataBytes > source.Length)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"Truncated Mode-2 sector at LBA {lba}: image may be incomplete.");
        }

        source.Seek(position, SeekOrigin.Begin);
        byte[] data = new byte[UserDataBytes];
        int totalRead = 0;
        while (totalRead < UserDataBytes)
        {
            int n = source.Read(data, totalRead, UserDataBytes - totalRead);
            if (n == 0)
            {
                throw new DiscSourceException(DiscSourceException.FormatInvalid,
                    $"Truncated Mode-2 sector at LBA {lba}: unexpected end of stream.");
            }

            totalRead += n;
        }

        return data;
    }

    private static void ValidatePvd(byte[] pvd)
    {
        // PVD: type=1, identifier="CD001", version=1
        if (pvd.Length < 7
            || pvd[0] != 1
            || pvd[1] != (byte)'C' || pvd[2] != (byte)'D'
            || pvd[3] != (byte)'0' || pvd[4] != (byte)'0' || pvd[5] != (byte)'1'
            || pvd[6] != 1)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                "Sector 16 is not an ISO9660 Primary Volume Descriptor.");
        }
    }

    private static List<(string Name, int Lba, int Parent)> ReadPathTable(Stream source, byte[] pvd)
    {
        // Path table size and L-type path table LBA are at fixed PVD offsets.
        int tableSize = ReadU32LE(pvd, 132);
        int tableLba = ReadU32LE(pvd, 140);

        if (tableSize == 0)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                "ISO9660 path table is empty.");
        }

        byte[] tableData = ReadContiguous(source, tableLba, tableSize);

        var entries = new List<(string Name, int Lba, int Parent)>();
        int cursor = 0;
        while (cursor < tableData.Length)
        {
            if (cursor + 8 > tableData.Length)
            {
                throw new DiscSourceException(DiscSourceException.FormatInvalid,
                    "Truncated ISO9660 path table entry.");
            }

            int nameLen = tableData[cursor];
            // Each entry is 8 fixed bytes + nameLen + optional padding byte.
            int recordSize = 8 + nameLen + (nameLen & 1);

            if (nameLen == 0 || cursor + recordSize > tableData.Length)
            {
                throw new DiscSourceException(DiscSourceException.FormatInvalid,
                    "Invalid ISO9660 path table entry: bad length.");
            }

            int lba = ReadU32LE(tableData, cursor + 2);
            int parent = BitConverter.ToUInt16(tableData, cursor + 6);
            string name = Encoding.ASCII.GetString(tableData, cursor + 8, nameLen);
            entries.Add((name, lba, parent));
            cursor += recordSize;
        }

        return entries;
    }

    /// <summary>
    /// Builds the relative directory path for the given 0-based path-table index.
    /// Root (index 0) returns an empty string.
    /// </summary>
    private static string BuildDirPath(IReadOnlyList<(string Name, int Lba, int Parent)> entries, int index)
    {
        if (index == 0)
        {
            return string.Empty;
        }

        var (name, _, parent) = entries[index];
        // Parent numbers in the path table are 1-indexed.
        if (parent <= 0 || parent - 1 >= index)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                "Invalid ISO9660 directory parent reference in path table.");
        }

        string parentPath = BuildDirPath(entries, parent - 1);
        return parentPath.Length == 0 ? name : parentPath + "/" + name;
    }

    /// <summary>
    /// Reads <paramref name="totalBytes"/> contiguous bytes starting at sector <paramref name="startLba"/>.
    /// </summary>
    private static byte[] ReadContiguous(Stream source, int startLba, int totalBytes)
    {
        int sectorsNeeded = (totalBytes + UserDataBytes - 1) / UserDataBytes;
        var buffer = new byte[sectorsNeeded * UserDataBytes];
        for (int i = 0; i < sectorsNeeded; i++)
        {
            byte[] sector = ReadSector(source, startLba + i);
            Array.Copy(sector, 0, buffer, i * UserDataBytes, UserDataBytes);
        }

        return buffer[..totalBytes];
    }

    /// <summary>Reads a little-endian 32-bit unsigned integer from <paramref name="data"/>.</summary>
    private static int ReadU32LE(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"ISO9660 field at offset {offset} lies outside available data.");
        }

        return (int)BitConverter.ToUInt32(data, offset);
    }

    // ── Directory traversal helpers ────────────────────────────────────────

    /// <summary>
    /// Reads all file entries from the directory at position <paramref name="dirIdx"/>
    /// in the path table and appends them to <paramref name="results"/>.
    /// </summary>
    /// <param name="source">The open BIN stream to read sectors from.</param>
    /// <param name="directories">The path-table directory list.</param>
    /// <param name="dirIdx">Zero-based index into <paramref name="directories"/>.</param>
    /// <param name="results">Accumulator that receives each discovered file entry.</param>
    private static void ReadDirectoryFiles(
        Stream source,
        List<(string Name, int Lba, int Parent)> directories,
        int dirIdx,
        List<DiscFileEntry> results)
    {
        var (_, dirLba, _) = directories[dirIdx];
        string dirPrefix = BuildDirPath(directories, dirIdx);

        // The first directory entry (".") carries the directory data length.
        byte[] firstBlock = ReadSector(source, dirLba);
        int dirSize = ReadU32LE(firstBlock, 10);

        // Read all directory data (may span multiple sectors).
        byte[] dirData = ReadContiguous(source, dirLba, dirSize);

        int cursor = 0;
        while (cursor < dirData.Length)
        {
            cursor = AdvanceCursor(dirData, cursor, dirPrefix, results);
        }
    }

    /// <summary>
    /// Processes a single directory record and returns the cursor position for the next record.
    /// Returns a sector-aligned position when the current record is a padding entry.
    /// </summary>
    /// <param name="dirData">The raw directory data bytes.</param>
    /// <param name="cursor">Current byte offset within <paramref name="dirData"/>.</param>
    /// <param name="dirPrefix">Slash-separated directory prefix for ISO paths.</param>
    /// <param name="results">Accumulator that receives file entries found in this record.</param>
    /// <returns>The byte offset of the next record to process.</returns>
    private static int AdvanceCursor(byte[] dirData, int cursor, string dirPrefix, List<DiscFileEntry> results)
    {
        int recordLen = dirData[cursor];
        if (recordLen == 0)
        {
            // Padding to next sector boundary.
            return ((cursor / UserDataBytes) + 1) * UserDataBytes;
        }

        ValidateDirectoryRecord(dirData, cursor, recordLen);
        int nameLen = dirData[cursor + 32];
        ValidateNameLength(nameLen, recordLen);

        byte flags = dirData[cursor + 25];
        bool isDirectory = (flags & 0x02) != 0;
        bool isDotEntry = nameLen == 1 && (dirData[cursor + 33] == 0x00 || dirData[cursor + 33] == 0x01);
        if (!isDirectory && !isDotEntry)
        {
            AddFileEntry(dirData, cursor, nameLen, dirPrefix, results);
        }

        return cursor + recordLen;
    }

    /// <summary>
    /// Throws <see cref="DiscSourceException"/> if the directory record at <paramref name="cursor"/>
    /// has an invalid length field or extends beyond the available data.
    /// </summary>
    /// <param name="dirData">The raw directory data bytes.</param>
    /// <param name="cursor">Current byte offset within <paramref name="dirData"/>.</param>
    /// <param name="recordLen">The record-length byte read from the record header.</param>
    private static void ValidateDirectoryRecord(byte[] dirData, int cursor, int recordLen)
    {
        if (cursor + recordLen > dirData.Length || recordLen < 34)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                "Malformed ISO9660 directory record: length out of range.");
        }
    }

    /// <summary>
    /// Throws <see cref="DiscSourceException"/> if the name-length field of a directory record
    /// exceeds the declared record length.
    /// </summary>
    /// <param name="nameLen">The name-length byte read from the record.</param>
    /// <param name="recordLen">The total record length.</param>
    private static void ValidateNameLength(int nameLen, int recordLen)
    {
        if (33 + nameLen > recordLen)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                "Malformed ISO9660 directory record: name exceeds record.");
        }
    }

    /// <summary>
    /// Decodes the file name from a directory record and appends a <see cref="DiscFileEntry"/>
    /// to <paramref name="results"/>.
    /// </summary>
    /// <param name="dirData">The raw directory data bytes.</param>
    /// <param name="cursor">Current byte offset within <paramref name="dirData"/>.</param>
    /// <param name="nameLen">The name length in bytes.</param>
    /// <param name="dirPrefix">Slash-separated directory prefix for the ISO path.</param>
    /// <param name="results">Accumulator that receives the new file entry.</param>
    private static void AddFileEntry(byte[] dirData, int cursor, int nameLen, string dirPrefix, List<DiscFileEntry> results)
    {
        string rawName = Encoding.ASCII.GetString(dirData, cursor + 33, nameLen);
        // Strip ISO9660 version suffix (e.g. ";1").
        string cleanName = rawName.Split(';')[0];
        string isoPath = dirPrefix.Length == 0 ? cleanName : dirPrefix + "/" + cleanName;

        int lba = ReadU32LE(dirData, cursor + 2);
        int size = ReadU32LE(dirData, cursor + 10);
        results.Add(new DiscFileEntry(isoPath, lba, size));
    }
}
