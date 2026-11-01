using System.Numerics;

namespace Dw3Production.Simulation;

/// <summary>
/// The connection-indicator view consumed by the renderer each frame: the current animation
/// frame index and the smooth anchor position (world pixels) that the frame's own source
/// offset is applied to.
/// </summary>
public sealed record IconView(int Frame, Vector2 Position);
