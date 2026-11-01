namespace Dw3Production.Simulation;

/// <summary>Actions that an enemy battle-table row can select.</summary>
public enum EnemyActionKind
{
    /// <summary>No action.</summary>
    None,
    /// <summary>Use the normal attack technique.</summary>
    Attack,
    /// <summary>Use the first special technique.</summary>
    Technique1,
    /// <summary>Use the second special technique.</summary>
    Technique2,
    /// <summary>Attempt to leave the battle.</summary>
    RunAway,
    /// <summary>Switch to enemy roster slot zero.</summary>
    SwitchTo0,
    /// <summary>Switch to enemy roster slot one.</summary>
    SwitchTo1,
    /// <summary>Switch to enemy roster slot two.</summary>
    SwitchTo2,
    /// <summary>Switch to any eligible enemy roster slot.</summary>
    SwitchToAny,
}
