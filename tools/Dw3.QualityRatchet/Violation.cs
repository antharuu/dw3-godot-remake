namespace Dw3.QualityRatchet;

/// <summary>
/// One analyzer result read from a SARIF log.
/// </summary>
/// <param name="RuleId">The diagnostic identifier, for example S3776.</param>
/// <param name="FilePath">The absolute path of the source file, or an empty string when the result has no location.</param>
/// <param name="Line">The first line of the reported region.</param>
/// <param name="Column">The first column of the reported region.</param>
internal readonly record struct Violation(string RuleId, string FilePath, int Line, int Column);
