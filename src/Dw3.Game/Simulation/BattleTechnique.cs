using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>One technique the active partner can pick in the Technique submenu.</summary>
public sealed record BattleTechnique(int Id, string Label, TechData Tech, BattleTextLine? Text = null);
