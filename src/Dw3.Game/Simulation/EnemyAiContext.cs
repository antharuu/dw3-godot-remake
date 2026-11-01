namespace Dw3Production.Simulation;

/// <summary>Battle state consulted while evaluating an enemy action table.</summary>
public sealed class EnemyAiContext
{
    /// <summary>Gets or sets the enemy whose action is being selected.</summary>
    public BattleFighter Enemy { get; set; } = new();

    /// <summary>Gets or sets the active player fighter, or <see langword="null"/> when absent.</summary>
    public BattleFighter? ActivePlayer { get; set; }

    /// <summary>Gets or sets the enemy roster, where a null entry is an empty slot.</summary>
    public IReadOnlyList<BattleFighter?> Enemies { get; set; } = [];

    /// <summary>Gets or sets the roster index of <see cref="Enemy"/>.</summary>
    public int ActiveEnemy { get; set; }

    /// <summary>Gets or sets the element currently boosted by the battlefield.</summary>
    public int BoostElement { get; set; }

    /// <summary>Gets or sets the current battle identifier.</summary>
    public int BattleId { get; set; }

    /// <summary>Gets or sets the current encounter category.</summary>
    public int EncounterKind { get; set; }

    /// <summary>Gets or sets the Digimon identifiers treated as starters by table conditions.</summary>
    public IReadOnlyCollection<int> StarterDigimonIds { get; set; } = [];
}
