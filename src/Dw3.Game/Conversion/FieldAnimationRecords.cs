namespace Dw3Production.Conversion;

// Engine-free runtime contracts of the field animation domains, read by FieldAnimationDataPackLoader.

/// <summary>One frame record of an action animation set: frame index, source-tick duration and
/// the sprite draw offset relative to the actor position.</summary>
public sealed record ClimbRecord(int Frame, int Duration, int OffsetX, int OffsetY);
/// <summary>One action animation set. <see cref="Loops"/> is false for the one-shot sets.</summary>
public sealed record ClimbSequence(int Set, bool Loops, IReadOnlyList<ClimbRecord> Records);
/// <summary>The action animation sets of one player action archive, keyed by set id.</summary>
public sealed record ClimbData(IReadOnlyDictionary<int, ClimbSequence> Sequences);
/// <summary>One exit-cue sprite frame and its draw offset.</summary>
public sealed record CueFrame(int Frame, string Path, int OffsetX, int OffsetY);
/// <summary>The exit-cue sprite frames and the inner two-frame loop of each field row.</summary>
public sealed record ExitCue(IReadOnlyDictionary<int, CueFrame> Frames, IReadOnlyDictionary<int, IReadOnlyList<(int Frame, int Duration)>> InnerRows);
/// <summary>One frame of an animated map object: its opaque and STP image layers.</summary>
public sealed record WaterFrame(string OpaquePath, string SemiPath, int OffsetX, int OffsetY, int Abr);
/// <summary>One animated map object of a stage and its frame cycle.</summary>
public sealed record WaterObject(int Index, int Margin, int Depth, int Cycle, int CycleFirst, int CycleLast, int CycleDelay, int Frame, int ClutRow, int CycleTime, int X, int Y, int SortY, IReadOnlyList<WaterFrame> Frames);
/// <summary>Animated map objects per zone.</summary>
public sealed record WaterData(IReadOnlyDictionary<string, IReadOnlyList<WaterObject>> Zones);
/// <summary>One decoded connection-indicator frame and its draw offset.</summary>
public sealed record PlayerIconFrame(int Frame, string Path, int OffsetX, int OffsetY);
/// <summary>One [frame, duration] step of an icon animation table; duration is in source ticks.</summary>
public sealed record PlayerIconStep(int Frame, int Duration);
/// <summary>One icon animation table: its steps and the step index its loop sentinel jumps to.</summary>
public sealed record PlayerIconAnimation(int Substate, IReadOnlyList<PlayerIconStep> Steps, int LoopTarget);
/// <summary>The connection-indicator frames 0x23..0x31 and the animation tables keyed by substate.</summary>
public sealed record PlayerIconData(IReadOnlyDictionary<int, PlayerIconFrame> Frames, IReadOnlyDictionary<int, PlayerIconAnimation> Animations);
