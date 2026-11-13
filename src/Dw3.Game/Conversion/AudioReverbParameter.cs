namespace Dw3Production.Conversion;

/// <summary>Global SPU reverb attribute selected by a SEP data-entry triplet (CC99/CC98/CC6).</summary>
public enum AudioReverbParameter
{
    /// <summary>libsnd DE_REV_TYPE (attribute 15): reverb preset, applied through SsUtSetReverbType.</summary>
    Type,

    /// <summary>libsnd DE_REV_DEPTH (attribute 16): reverb output depth 0-127, applied to both sides.</summary>
    Depth,
}
