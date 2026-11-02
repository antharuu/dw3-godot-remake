namespace Dw3Production.Conversion;

/// <summary>One [frame, duration] step of an icon animation table; duration is in source ticks.</summary>
public sealed record PlayerIconStep(int Frame, int Duration);
