namespace Dw3Production.Conversion;

/// <summary>Runtime availability of one script-visible sprite identity in the prepared index.</summary>
public enum BattleSpriteAvailability
{
    /// <summary>Every referenced frame was decoded and validated, the effect can be drawn.</summary>
    Available,

    /// <summary>The source archive entry is qualified as empty: the source task ends at once.</summary>
    SourceAbsent,

    /// <summary>The source could not be represented self-containedly. Never drawn or acknowledged.</summary>
    Unsupported,

    /// <summary>The identity is absent from the prepared index: an incomplete pack, not a source absence.</summary>
    Missing,
}
