namespace Dw3.Extractor.Tests;

/// <summary>
/// Builds a fabricated disc whose field-stage registry holds the 236 explorable stages the zone
/// pass expects: FIELDSTG.PRO lists one record per stage (plus one demonstration record of mode
/// 0x204), the executable FILE_SECTORS table points overlay file ids at WSTAGnnn.PRO members, and
/// every overlay starts with the TMPK setup calls that select floor entry 2 and trigger entry 1.
/// </summary>
internal static class FabricatedFieldDisc
{
    internal const int FirstStage = 200;
    internal const int StageCount = 236;
    internal const int FirstOverlayFileId = 466;
    internal const int DemonstrationStage = 199;
    internal const uint OverlayBase = 0x800A5DE0;

    // File offset of the executable's special-condition table; its first byte 0xFF ends it empty.
    private const int SpecialConditionsOffset = 0x392D8;
    private const int StageTableOffset = 0x17BD4;
    private const int StageRecordBytes = 12;

    /// <summary>Gets the stage ids of the registry, in table order.</summary>
    internal static IEnumerable<int> Stages => Enumerable.Range(FirstStage, StageCount);

    /// <summary>
    /// Creates the disc. <paramref name="overlay"/> builds the bytes of each stage overlay (defaults
    /// to <see cref="Overlay"/>); <paramref name="executable"/> receives the fabricated executable
    /// before any file is added so tests can add their own tables. With <paramref name="stageSprites"/>,
    /// every stage also gets one-byte SnnnTMPK.BIN and SnnnMASK.BIN members.
    /// </summary>
    internal static FabricatedDisc Create(Func<int, byte[]>? overlay = null, Action<byte[]>? executable = null, bool stageSprites = false)
    {
        byte[] exe = Fabricate.Executable(0x80050000);
        exe[SpecialConditionsOffset] = 0xFF;
        executable?.Invoke(exe);
        var fieldStage = new byte[0x20000];
        var disc = new FabricatedDisc().Add("SLES_039.36", exe).Add("AAA/PRO/FIELDSTG.PRO", fieldStage);
        int record = 0;
        AddStage(disc, exe, fieldStage, record++, DemonstrationStage, 0x204, overlay ?? (_ => Overlay()));
        foreach (int stage in Stages)
        {
            AddStage(disc, exe, fieldStage, record++, stage, (ushort)(0x300 + stage), overlay ?? (_ => Overlay()));
            if (stageSprites)
            {
                disc.Add($"AAA/DAT/FIELD/STAGE/S{stage:D3}TMPK.BIN", [1]).Add($"AAA/DAT/FIELD/STAGE/S{stage:D3}MASK.BIN", [1]);
            }
        }

        return disc;
    }

    /// <summary>
    /// Returns an overlay whose code passes a0 = 0 / a1 = (0x0197 &lt;&lt; 16) | 2 and then
    /// a0 = 7 / a1 = (0x0197 &lt;&lt; 16) | 1 to an indirect call, the setup the TMPK selection reader recovers.
    /// </summary>
    internal static byte[] Overlay(int size = 0x400)
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x0197), Ori(5, 5, 2), 0u,
            Addiu(4, 0, 7), Lui(5, 0x0197), Ori(5, 5, 1), Jalr(2), 0u,
        ];
        var overlay = new byte[size];
        for (int index = 0; index < words.Length; index++)
        {
            Fabricate.U32(overlay, index * 4, words[index]);
        }

        return overlay;
    }

    private static void AddStage(FabricatedDisc disc, byte[] exe, byte[] fieldStage, int record, int stage, ushort mode, Func<int, byte[]> overlay)
    {
        string path = $"AAA/PRO/WSTAG{stage:D3}.PRO";
        disc.Add(path, overlay(stage));
        int fileId = FirstOverlayFileId + stage - FirstStage;
        Fabricate.FileSector(exe, fileId, disc.LbaOf(path));
        int offset = StageTableOffset + (record * StageRecordBytes);
        Fabricate.U32(fieldStage, offset, mode);
        Fabricate.U32(fieldStage, offset + 4, (uint)fileId);
        Fabricate.U32(fieldStage, offset + 8, OverlayBase);
    }

    private static uint Addiu(int rt, int rs, int immediate) => 0x24000000u | ((uint)rs << 21) | ((uint)rt << 16) | (ushort)immediate;

    private static uint Lui(int rt, int immediate) => 0x3C000000u | ((uint)rt << 16) | (ushort)immediate;

    private static uint Ori(int rt, int rs, int immediate) => 0x34000000u | ((uint)rs << 21) | ((uint)rt << 16) | (ushort)immediate;

    private static uint Jalr(int rs) => ((uint)rs << 21) | (31u << 11) | 0x09u;
}
