namespace Dw3Production.Simulation;

/// <summary>Describes one item-tech request before a session inventory commits any stock change.</summary>
/// <param name="Owner">Side submitting the item-tech action.</param>
/// <param name="TechniqueId">Source technique identifier selecting the item-tech family and scope.</param>
/// <param name="Effect">Extracted effect identifier expected for <paramref name="TechniqueId"/>.</param>
/// <param name="HealAmount">Already-resolved HP amount for healing item-techs.</param>
public readonly record struct BattleItemTechniqueRequest(BattleSide Owner, int TechniqueId, int Effect, int HealAmount);
