namespace Dw3Production.Simulation;

/// <summary>Maps one control status to its fighter flag, expiry event and paired resistance slots.</summary>
/// <param name="Flag">Fighter flag marking the status.</param>
/// <param name="ExpiryKind">Event that ends the status.</param>
/// <param name="StatusResistance">Named resistance slot.</param>
/// <param name="ElementResistance">Paired elemental resistance slot.</param>
public readonly record struct BattleControlStatusDefinition(int Flag, BattleScheduledEventKind ExpiryKind, int StatusResistance, int ElementResistance);
