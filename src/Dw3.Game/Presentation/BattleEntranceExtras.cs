namespace Dw3Production.Presentation;

/// <summary>
/// What a fighter's own entrance brings with it in the source battle. Fighter 0x1D2 changes the fight stage to 0x16
/// under the full white and holds its view for only ten time units, fighter 0x1D3 fades the last boss music out
/// before its entrance and starts the last boss's second music under the full white. Every other fighter brings
/// nothing and holds for 120 units.
/// </summary>
/// <param name="StageId">Fight stage set under the full white, or -1 to keep the current one.</param>
/// <param name="FadedMusicId">Encoded music faded out before the entrance, or zero.</param>
/// <param name="MusicId">Encoded music started under the full white, or zero.</param>
/// <param name="HoldFrames">Time units the incoming view holds after the flash before the camera returns.</param>
internal sealed record BattleEntranceExtras(int StageId, uint FadedMusicId, uint MusicId, int HoldFrames)
{
    /// <summary>Fighter whose entrance changes the fight stage.</summary>
    public const int StageFighterId = 0x1D2;

    /// <summary>Fighter whose entrance changes the music.</summary>
    public const int MusicFighterId = 0x1D3;

    /// <summary>Fight stage set by <see cref="StageFighterId"/>'s entrance.</summary>
    public const int EntranceStageId = 0x16;

    /// <summary>Encoded last boss music, bank 0x24, faded out by <see cref="MusicFighterId"/>'s entrance.</summary>
    public const uint LastBossMusic = 0x60000000u | (0x24u << 18);

    /// <summary>Encoded last boss second-form music, bank 0x26, started by <see cref="MusicFighterId"/>'s entrance.</summary>
    public const uint LastBossSecondMusic = 0x60000000u | (0x26u << 18);

    /// <summary>Ordinary hold of the incoming view.</summary>
    public const int DefaultHoldFrames = 120;

    /// <summary>Short hold of <see cref="StageFighterId"/>'s entrance.</summary>
    public const int StageFighterHoldFrames = 10;

    /// <summary>Gets the extras of an ordinary entrance.</summary>
    public static BattleEntranceExtras None { get; } = new(-1, 0, 0, DefaultHoldFrames);

    /// <summary>Gets a value indicating whether the entrance changes the fight stage.</summary>
    public bool ChangesStage => StageId >= 0;

    /// <summary>Gets a value indicating whether the entrance changes the music.</summary>
    public bool ChangesMusic => MusicId != 0;

    /// <summary>Returns the extras the source attaches to an incoming fighter id.</summary>
    /// <param name="fighterId">Incoming fighter id.</param>
    /// <returns>The fighter's extras, or <see cref="None"/>.</returns>
    public static BattleEntranceExtras For(int fighterId) => fighterId switch
    {
        StageFighterId => new(EntranceStageId, 0, 0, StageFighterHoldFrames),
        MusicFighterId => new(-1, LastBossMusic, LastBossSecondMusic, DefaultHoldFrames),
        _ => None,
    };
}
