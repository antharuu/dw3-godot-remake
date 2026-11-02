namespace Dw3Production.Conversion;

/// <summary>Animated map objects per zone.</summary>
public sealed record WaterData(IReadOnlyDictionary<string, IReadOnlyList<WaterObject>> Zones);
