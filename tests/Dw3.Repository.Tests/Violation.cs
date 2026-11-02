namespace Dw3.Repository.Tests;

/// <summary>A single guard violation: the tracked file and the rule that failed.</summary>
public sealed record Violation(string Path, string Rule, string Note);
