namespace Dw3Production.Presentation;

/// <summary>The stages of an entrance as its source caller runs it, in playing order.</summary>
internal enum BattleEntranceCallPhase
{
    /// <summary>The caller's message before the entrance waits for Cross, such as a partner being called back.</summary>
    Before,

    /// <summary>The entrance itself plays.</summary>
    Sequence,

    /// <summary>The caller's message naming the incoming fighter waits for Cross while the camera returns.</summary>
    After,

    /// <summary>The caller released the battle to its next turn.</summary>
    Done,
}
