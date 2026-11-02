namespace Dw3Production.Conversion;

/// <summary>One animated map object of a stage and its frame cycle.</summary>
public sealed record WaterObject(int Index, int Margin, int Depth, int Cycle, int CycleFirst, int CycleLast, int CycleDelay, int Frame, int ClutRow, int CycleTime, int X, int Y, int SortY, IReadOnlyList<WaterFrame> Frames);
