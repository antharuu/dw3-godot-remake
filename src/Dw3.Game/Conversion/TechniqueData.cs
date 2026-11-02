namespace Dw3Production.Conversion;

/// <summary>The m6-tech-v1 contract: every technique keyed by id.</summary>
public sealed record TechniqueData(IReadOnlyDictionary<int, TechData> Techs);
