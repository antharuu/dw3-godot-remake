namespace Dw3.Data;

/// <summary>
/// Describes the source and completion identity persisted with a data pack.
/// </summary>
/// <param name="ExtractorVersion">Version of the extractor that produced the pack.</param>
/// <param name="DiscSha1">SHA-1 fingerprint of the user-supplied disc image.</param>
/// <param name="Edition">Edition identifier recognized by the extractor.</param>
/// <param name="CompletedAt">UTC timestamp at which validation completed.</param>
public sealed record DataPackMetadata(
    string ExtractorVersion,
    string DiscSha1,
    string Edition,
    DateTimeOffset CompletedAt);
