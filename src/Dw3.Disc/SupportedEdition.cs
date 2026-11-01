namespace Dw3.Disc;

/// <summary>
/// Identifies a known supported game edition by its disc fingerprint.
/// No game-derived bytes are embedded: only cryptographic hashes of the user's disc.
/// </summary>
/// <param name="Label">Human-readable edition name.</param>
/// <param name="BinMd5">MD5 hex digest of the BIN file (lower-case).</param>
/// <param name="BinSha1">SHA-1 hex digest of the BIN file (lower-case).</param>
public sealed record SupportedEdition(string Label, string BinMd5, string BinSha1);
