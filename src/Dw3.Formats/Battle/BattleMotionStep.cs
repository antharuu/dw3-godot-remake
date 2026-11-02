namespace Dw3.Formats.Battle;

/// <summary>
/// One step in a battle motion sequence, decoded from an 8-byte entry (4 × s16) in the motion
/// archive. The sequence ends when <see cref="Index"/> equals 0x7FFF (the terminator sentinel).
/// </summary>
/// <param name="Index">Motion index to play, or 0x7FFF to mark the end of the sequence.</param>
/// <param name="Count">Number of sub-steps or repetitions for this motion entry.</param>
/// <param name="Frame">Start frame within the referenced motion clip.</param>
/// <param name="EndFrame">Inclusive end frame within the referenced motion clip.</param>
public sealed record BattleMotionStep(int Index, int Count, int Frame, int EndFrame);
