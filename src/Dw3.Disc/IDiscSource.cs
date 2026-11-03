namespace Dw3.Disc;

/// <summary>
/// Read-only view of a mounted disc: the ISO9660 file listing, the user-data bytes of each
/// file and the edition check. <see cref="DiscSource"/> reads a BIN/CUE image; the extractor
/// depends on this contract so that its steps can run against an in-memory disc built from
/// fabricated files, without the user's image.
/// </summary>
public interface IDiscSource : IDisposable
{
    /// <summary>Reads the ISO9660 volume identifier from the Primary Volume Descriptor.</summary>
    /// <returns>The volume identifier trimmed of trailing spaces (<c>"DMW3"</c> on the supported disc).</returns>
    /// <exception cref="DiscSourceException">The volume descriptor cannot be read.</exception>
    string ReadVolumeId();

    /// <summary>Lists every file of the ISO9660 directory tree.</summary>
    /// <returns>All file entries in path-table traversal order, with their LBA and byte size.</returns>
    /// <exception cref="DiscSourceException">The directory tree is malformed or truncated.</exception>
    IReadOnlyList<DiscFileEntry> ListFiles();

    /// <summary>Reads the user-data bytes of one file.</summary>
    /// <param name="entry">An entry returned by <see cref="ListFiles"/>, giving the first LBA and the byte size.</param>
    /// <returns>Exactly <see cref="DiscFileEntry.Size"/> bytes; an empty array for a zero-length file.</returns>
    /// <exception cref="DiscSourceException">The sectors of the file cannot be read.</exception>
    byte[] ReadFile(DiscFileEntry entry);

    /// <summary>Checks that the disc is a supported edition and returns its fingerprint record.</summary>
    /// <returns>The edition whose label and BIN SHA-1 are stamped into every data pack.</returns>
    /// <exception cref="DiscSourceException">The disc is not a supported edition or not a readable ISO9660 image.</exception>
    SupportedEdition IdentifyEdition();
}
