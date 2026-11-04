namespace Dw3Production.Presentation;

/// <summary>One effective source-bone transform captured during an end-to-end motion cadence probe.</summary>
/// <param name="Index">Source bone index.</param>
/// <param name="Visible">Whether the source scale threshold leaves this bone drawable.</param>
/// <param name="OriginX">World transform origin X.</param>
/// <param name="OriginY">World transform origin Y.</param>
/// <param name="OriginZ">World transform origin Z.</param>
/// <param name="BasisXx">World transform basis X.X.</param>
/// <param name="BasisXy">World transform basis X.Y.</param>
/// <param name="BasisXz">World transform basis X.Z.</param>
/// <param name="BasisYx">World transform basis Y.X.</param>
/// <param name="BasisYy">World transform basis Y.Y.</param>
/// <param name="BasisYz">World transform basis Y.Z.</param>
/// <param name="BasisZx">World transform basis Z.X.</param>
/// <param name="BasisZy">World transform basis Z.Y.</param>
/// <param name="BasisZz">World transform basis Z.Z.</param>
public sealed record BattleMotionCadenceBone(
    int Index,
    bool Visible,
    float OriginX,
    float OriginY,
    float OriginZ,
    float BasisXx,
    float BasisXy,
    float BasisXz,
    float BasisYx,
    float BasisYy,
    float BasisYz,
    float BasisZx,
    float BasisZy,
    float BasisZz);
