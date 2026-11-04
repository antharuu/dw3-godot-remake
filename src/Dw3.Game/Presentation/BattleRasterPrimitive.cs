using Godot;

namespace Dw3Production.Presentation;

/// <summary>Retains one collected source primitive before its disjoint GPU coverage commands.</summary>
/// <param name="Key">Unsplit source ordering-table key.</param>
/// <param name="Polygon">Source corners, texture key and runtime ABR selection.</param>
/// <param name="Screen">Projected corners in viewport pixels.</param>
/// <param name="Texture">Live decoded texture page including raw alpha classes.</param>
/// <param name="Fighter">Fighter identifier owning the command.</param>
internal readonly record struct BattleRasterPrimitive(int Key, BattleModel.SourcePolygon Polygon, Vector2[] Screen, Texture2D Texture, int Fighter);
