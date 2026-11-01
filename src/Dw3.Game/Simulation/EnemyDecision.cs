namespace Dw3Production.Simulation;

/// <summary>A resolved enemy action and its optional technique or roster slot.</summary>
public readonly record struct EnemyDecision(EnemyActionKind Kind, int TechId, int SwitchSlot)
{
    /// <summary>Gets the decision that performs no action.</summary>
    public static EnemyDecision None => new(EnemyActionKind.None, 0, -1);

    /// <summary>Gets a value indicating whether gets whether this is a normal attack.</summary>
    public bool IsAttack => Kind == EnemyActionKind.Attack;

    /// <summary>Gets a value indicating whether gets whether this uses either special technique.</summary>
    public bool IsTechnique => Kind is EnemyActionKind.Technique1 or EnemyActionKind.Technique2;

    /// <summary>Gets a value indicating whether gets whether this requests an enemy switch.</summary>
    public bool IsSwitch => Kind is >= EnemyActionKind.SwitchTo0 and <= EnemyActionKind.SwitchToAny;

    /// <summary>Gets a value indicating whether gets whether this requests retreat from battle.</summary>
    public bool IsRunAway => Kind == EnemyActionKind.RunAway;
}
