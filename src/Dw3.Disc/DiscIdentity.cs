using System.Security.Cryptography;

namespace Dw3.Disc;

/// <summary>
/// Matches a BIN file against the table of known supported editions and computes disc digests.
/// </summary>
public static class DiscIdentity
{
    // ── Known editions ─────────────────────────────────────────────────────

    /// <summary>
    /// The only supported edition: Europe SLES-03936 (DMW3).
    /// These are public fingerprints derived from the user's own disc; no game content is stored.
    /// </summary>
    public static readonly IReadOnlyList<SupportedEdition> KnownEditions =
    [
        new SupportedEdition(
            Label: "Europe SLES-03936 (DMW3)",
            BinMd5: "da76a5685b875c353343102b7587fded",
            BinSha1: "457cb233349ba841e03b33d8060f8fbcadd45cb3")
    ];

    // ── Edition lookup ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the matching <see cref="SupportedEdition"/> or throws
    /// <see cref="DiscSourceException"/> with <see cref="DiscSourceException.EditionInvalid"/>
    /// if no edition matches.
    /// </summary>
    /// <param name="binMd5">MD5 hex digest of the BIN file (lower-case, 32 characters).</param>
    /// <param name="binSha1">SHA-1 hex digest of the BIN file (lower-case, 40 characters).</param>
    /// <returns>The <see cref="SupportedEdition"/> whose MD5 and SHA-1 match the supplied digests.</returns>
    /// <exception cref="DiscSourceException">
    /// Raised with <see cref="DiscSourceException.EditionInvalid"/> when no known edition matches both digests.
    /// </exception>
    public static SupportedEdition Identify(string binMd5, string binSha1)
    {
        foreach (var edition in KnownEditions)
        {
            if (string.Equals(edition.BinMd5, binMd5, StringComparison.OrdinalIgnoreCase)
             && string.Equals(edition.BinSha1, binSha1, StringComparison.OrdinalIgnoreCase))
            {
                return edition;
            }
        }

        throw new DiscSourceException(DiscSourceException.EditionInvalid,
            "BIN digest does not match the supported Europe SLES-03936 edition; " +
            "provide the documented BIN/CUE pair.");
    }

    // ── Hashing helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Computes MD5, SHA-1 and SHA-256 of a file in a single streaming pass.
    /// Returns all three as lower-case hex strings.
    /// </summary>
    /// <param name="path">Absolute path to the file to hash; must be readable.</param>
    /// <returns>
    /// A tuple of (MD5, SHA-1, SHA-256), each as a lower-case hex string
    /// (32, 40, and 64 characters respectively).
    /// </returns>
    public static (string Md5, string Sha1, string Sha256) DigestFile(string path)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        const int BufferSize = 1 << 20; // 1 MiB
        byte[] buffer = new byte[BufferSize];
        using var stream = File.OpenRead(path);
        int read;
        while ((read = stream.Read(buffer, 0, BufferSize)) > 0)
        {
            md5.AppendData(buffer, 0, read);
            sha1.AppendData(buffer, 0, read);
            sha256.AppendData(buffer, 0, read);
        }

        return (
            Convert.ToHexStringLower(md5.GetHashAndReset()),
            Convert.ToHexStringLower(sha1.GetHashAndReset()),
            Convert.ToHexStringLower(sha256.GetHashAndReset()));
    }

    /// <summary>
    /// Computes the SHA-256 hex digest of an in-memory byte array (lower-case).
    /// </summary>
    /// <param name="data">Byte span to hash; may be empty, in which case the hash of zero bytes is returned.</param>
    /// <returns>SHA-256 digest as a 64-character lower-case hex string.</returns>
    public static string Sha256Bytes(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));
}
