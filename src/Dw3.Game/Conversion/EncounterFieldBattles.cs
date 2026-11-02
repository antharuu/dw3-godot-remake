namespace Dw3Production.Conversion;

/// <summary>One FieldBattles place: four battle areas (areas[3] is the event battle area).</summary>
public sealed record EncounterFieldBattles(int Serial, int PlaceId, int Pad8, IReadOnlyList<EncounterBattleList> Areas);
