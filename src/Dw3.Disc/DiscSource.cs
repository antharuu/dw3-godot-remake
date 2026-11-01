namespace Dw3.Disc;

/// <summary>
/// Opens a BIN/CUE disc image for read-only ISO9660 access.
/// The stream is held open for the lifetime of the instance.
/// Dispose to release the file handle.
/// </summary>
public sealed class DiscSource : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _binPath;
    private bool _disposed;

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
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"Cannot open BIN file: {binPath}", ex);
        }
        return new DiscSource(stream, binPath);
    }

    // ── Read-only operations ───────────────────────────────────────────────

    /// <summary>Reads the ISO9660 volume identifier from the Primary Volume Descriptor.</summary>
    public string ReadVolumeId() => Iso9660Reader.ReadVolumeId(_stream);

    /// <summary>
    /// Returns all files on the disc in path-table traversal order.
    /// </summary>
    public IReadOnlyList<DiscFileEntry> ListFiles() => Iso9660Reader.ListFiles(_stream);

    /// <summary>
    /// Reads the raw user-data bytes of a disc file.
    /// </summary>
    public byte[] ReadFile(DiscFileEntry entry) =>
        Iso9660Reader.ReadFileContent(_stream, entry.Lba, entry.Size);

    // ── Edition validation ─────────────────────────────────────────────────

    /// <summary>
    /// Validates the BIN fingerprint and volume ID against all known supported editions.
    /// Returns the matching <see cref="SupportedEdition"/>.
    /// </summary>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> if the BIN is not a valid Mode-2 ISO9660 image.
    /// <see cref="DiscSourceException.EditionInvalid"/> if the fingerprint or volume ID does not match.
    /// </exception>
    public SupportedEdition IdentifyEdition()
    {
        var (md5, sha1, _) = DiscIdentity.DigestFile(_binPath);
        SupportedEdition edition = DiscIdentity.Identify(md5, sha1);

        string volumeId;
        try { volumeId = ReadVolumeId(); }
        catch (DiscSourceException ex)
        {
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"BIN is not a supported Mode-2 ISO9660 disc: {ex.Message}", ex);
        }
        if (volumeId != "DMW3")
            throw new DiscSourceException(DiscSourceException.EditionInvalid,
                $"Disc volume ID is '{volumeId}', expected 'DMW3'.");
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
