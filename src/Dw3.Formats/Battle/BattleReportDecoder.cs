using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes the STFGTREP result-screen sprite sheet (file 0x79F) and its companion assets.
///
/// Source layout (report.c / partner.c EU):
/// <list type="bullet">
///   <item>File 0x79F: one-entry archive; entry 0 is the SpriteDrawer sprite sheet.</item>
///   <item>File 0x7A0: multi-entry model-archive of RLEN TIMs, uploaded at VRAM (0x280, 0).</item>
///   <item>CMFONTCS (file 0x286): menu sprite sheet — the arrow frame (id 10) is borrowed.</item>
///   <item>CMFONTTM (file 0x287): menu texture archive — provides the arrow palette.</item>
///   <item>STFGTREP.PRO overlay: the STFGTREP_animations table at 0x8008905C.</item>
/// </list>
/// </summary>
public static class BattleReportDecoder
{
    // Engine file ids (EU disc).
    public const int SheetFileId = 0x79F;
    public const int TextureFileId = 0x7A0;
    /// <summary>Disc path of the STFGTREP overlay, for documentation purposes.</summary>
    public const string OverlayDiscPath = "AAA/PRO/STFGTREP.PRO";

    // Overlay load base and animation table address (EU).
    public const uint OverlayBase = 0x80082CB0;
    public const uint AnimationsAddr = 0x8008905C;

    // 8 partners × 7 frame slots (s32 each), terminated with -1.
    public const int AnimationPartners = 8;
    public const int AnimationFrames = 7;

    // Arrow frame from CMFONTCS: MESSAGE_ARROW_FRAME = 10, 5 CLUT rows.
    public const int ArrowFrameId = 10;
    public const int ArrowRows = 5;

    /// <summary>
    /// Per-frame row counts: frames 0x00..0x1C have 1 CLUT row; 0x1D..0x23 have 11 rows;
    /// 0x24 and 0x25 have 4 rows.  Keyed by frame id.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int> RequiredFrameRows = BuildRequiredRows();

    // Pinned animation sequences from STFGTREP_animations (EU disc).
    private static readonly int[,] PinnedAnimations =
    {
        { 7, 8, 9, 10, 9, 8, -1 },
        { 14, 15, 16, 15, -1, -1, -1 },
        { 11, 12, 13, 12, -1, -1, -1 },
        { 3, 4, 5, 6, 5, 4, -1 },
        { 25, 26, 27, 28, 27, 26, -1 },
        { 0, 1, 2, 1, -1, -1, -1 },
        { 17, 18, 19, 20, 19, 18, -1 },
        { 21, 22, 23, 24, 23, 22, -1 },
    };

    private static IReadOnlyDictionary<int, int> BuildRequiredRows()
    {
        var d = new Dictionary<int, int>();
        for (int f = 0x00; f <= 0x1C; f++) d[f] = 1;
        for (int f = 0x1D; f <= 0x23; f++) d[f] = 11;
        d[0x24] = 4;
        d[0x25] = 4;
        return d;
    }

    /// <summary>
    /// Reads and validates the STFGTREP_animations table from the STFGTREP.PRO overlay.
    /// The table is 8 rows × 7 s32 values at address 0x8008905C (EU overlay base 0x80082CB0).
    /// Throws <see cref="FormatException"/> if the table is absent or differs from the pinned EU values.
    /// </summary>
    public static int[,] ReadAnimations(byte[] overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        int offset = (int)(AnimationsAddr - OverlayBase);
        int size = AnimationPartners * AnimationFrames * 4;
        if (offset < 0 || offset + size > overlay.Length)
            throw new FormatException($"STFGTREP_animations at 0x{AnimationsAddr:X} lies outside STFGTREP.PRO.");

        var result = new int[AnimationPartners, AnimationFrames];
        for (int p = 0; p < AnimationPartners; p++)
            for (int f = 0; f < AnimationFrames; f++)
                result[p, f] = BinaryPrimitives.ReadInt32LittleEndian(
                    overlay.AsSpan(offset + (p * AnimationFrames + f) * 4));

        // Verify against pinned EU source.
        for (int p = 0; p < AnimationPartners; p++)
            for (int f = 0; f < AnimationFrames; f++)
                if (result[p, f] != PinnedAnimations[p, f])
                    throw new FormatException(
                        $"STFGTREP_animations[{p},{f}] is {result[p, f]}, expected {PinnedAnimations[p, f]} (EU).");

        // Every row must have a -1 terminator before index 7.
        for (int p = 0; p < AnimationPartners; p++)
        {
            bool found = false;
            for (int f = 0; f < AnimationFrames; f++) if (result[p, f] == -1) { found = true; break; }
            if (!found) throw new FormatException($"STFGTREP_animations row {p} has no -1 terminator.");
        }

        return result;
    }

    /// <summary>Returns the index of <paramref name="frameId"/> in <paramref name="ids"/>.</summary>
    public static int FramePosition(IReadOnlyList<int> ids, int frameId)
    {
        for (int i = 0; i < ids.Count; i++)
            if (ids[i] == frameId) return i;
        throw new FormatException($"Sprite sheet lacks required frame id {frameId}.");
    }
}
