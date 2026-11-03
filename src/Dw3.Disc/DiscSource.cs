namespace Dw3.Disc;

/// <summary>
/// Opens a BIN/CUE disc image for read-only ISO9660 access.
/// The stream is held open for the lifetime of the instance.
/// Dispose to release the file handle.
/// </summary>
public sealed class DiscSource : IDiscSource
{
    private readonly FileStream _stream;
    private readonly string _binPath;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscSource"/> class, taking ownership
    /// of the already-opened BIN file stream and recording the BIN path for later fingerprinting.
    /// </summary>
    /// <param name="stream">Open read-only file stream for the BIN image; must not be disposed by the caller.</param>
    /// <param name="binPath">Absolute path to the BIN file, used for fingerprinting in <see cref="IdentifyEdition"/>.</param>
    private DiscSource(FileStream stream, string binPath)
    {
        _stream = stream;
        _binPath = binPath;
    }

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the BIN/CUE pair described by the given CUE sheet path.
    /// </summary>
    /// <param name="cuePath">Absolute or relative path to the <c>.cue</c> file.</param>
    /// <returns>A <see cref="DiscSource"/> holding an open read-only stream to the BIN file. Caller must dispose it.</returns>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> if the CUE or BIN cannot be opened.
    /// </exception>
    public static DiscSource Open(string cuePath)
    {
        string binPath = CueParser.ResolveBinPath(cuePath);
        FileStream stream;
        try
        {
            stream = new FileStream(binPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"Cannot open BIN file: {binPath}",
                ex);
        }

        return new DiscSource(stream, binPath);
    }

    // ── Read-only operations ───────────────────────────────────────────────

    /// <summary>Reads the ISO9660 volume identifier from the Primary Volume Descriptor.</summary>
    /// <returns>The volume identifier string trimmed of trailing spaces (e.g. <c>"DMW3"</c>).</returns>
    public string ReadVolumeId() => Iso9660Reader.ReadVolumeId(_stream);

    /// <summary>
    /// Returns all files on the disc in path-table traversal order.
    /// </summary>
    /// <returns>All file entries found in the ISO9660 directory tree, in path-table traversal order.</returns>
    public IReadOnlyList<DiscFileEntry> ListFiles() => Iso9660Reader.ListFiles(_stream);

    /// <summary>
    /// Reads the raw user-data bytes of a disc file.
    /// </summary>
    /// <param name="entry">File entry obtained from <see cref="ListFiles"/>; provides the LBA and size.</param>
    /// <returns>The raw user-data bytes of the file. Returns an empty array when the file size is zero.</returns>
    public byte[] ReadFile(DiscFileEntry entry) =>
        Iso9660Reader.ReadFileContent(_stream, entry.Lba, entry.Size);

    // ── Edition validation ─────────────────────────────────────────────────

    /// <summary>
    /// Validates the BIN fingerprint and volume ID against all known supported editions.
    /// Returns the matching <see cref="SupportedEdition"/>.
    /// </summary>
    /// <returns>The <see cref="SupportedEdition"/> record whose MD5 and SHA-1 match this disc's BIN file.</returns>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> if the BIN is not a valid Mode-2 ISO9660 image.
    /// <see cref="DiscSourceException.EditionInvalid"/> if the fingerprint or volume ID does not match.
    /// </exception>
    public SupportedEdition IdentifyEdition()
    {
        var (md5, sha1, _) = DiscIdentity.DigestFile(_binPath);
        SupportedEdition edition = DiscIdentity.Identify(md5, sha1);

        string volumeId;
        try
        {
            volumeId = ReadVolumeId();
        }
        catch (DiscSourceException ex)
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"BIN is not a supported Mode-2 ISO9660 disc: {ex.Message}",
                ex);
        }

        if (volumeId != "DMW3")
        {
            throw new DiscSourceException(
                DiscSourceException.EditionInvalid,
                $"Disc volume ID is '{volumeId}', expected 'DMW3'.");
        }

        return edition;
    }

    // ── IDisposable ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _stream.Dispose();
            _disposed = true;
        }
    }
}
