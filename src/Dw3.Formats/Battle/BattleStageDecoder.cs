using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes FightStageInfo records from the FILE_FIGHT_STAGES binary (engine file 0x1CB, EU).
/// Each record describes one of the 27 battle background scenes.
/// </summary>
public static class BattleStageDecoder
{
    /// <summary>Engine file id of the FightStageInfo table (EU disc).</summary>
    public const int FightStageFileId = 0x1CB;

    /// <summary>Number of fight stage records in the EU disc.</summary>
    public const int StageCount = 27;

    // sizeof(FightStageInfo) from stage.h — model, motions, music, pad, bgColor, LightSet, ...
    private const int Stride = 0x58;

    // Byte offsets within one FightStageInfo record.
    private const int OffModel = 0x00;   // u32 high16=fileId, low16=archiveEntry
    private const int OffMotions = 0x04; // u32 same layout
    private const int OffMusic = 0x08;   // s32
    private const int OffBgColor = 0x0C; // 3 bytes R,G,B
    private const int OffLights = 0x18;  // 3 × 16 bytes (GsF_LIGHT padded to 16 by PS1 ABI)
    private const int OffAmbient = 0x48; // 3 × s32 passed to SetBackColor

    /// <summary>
    /// Parses one FightStageInfo record.
    /// </summary>
    /// <param name="data">Raw bytes of the complete FILE_FIGHT_STAGES file.</param>
    /// <param name="stageId">Stage index, 0..26.</param>
    /// <exception cref="FormatException">Record out of bounds or id out of range.</exception>
    public static BattleStageInfo ParseRecord(byte[] data, int stageId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (stageId < 0 || stageId >= StageCount)
        {
            throw new FormatException($"Fight stage id {stageId} is out of range (must be 0..{StageCount - 1}).");
        }

        int offset = stageId * Stride;
        if (offset + Stride > data.Length)
        {
            throw new FormatException($"Fight stage record {stageId} overruns the file (file length {data.Length}).");
        }

        // Model and motions are packed as high16=fileId, low16=archiveEntry.
        uint model = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + OffModel));
        uint motions = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + OffMotions));
        int music = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + OffMusic));

        // bgColor: 3 raw bytes (R, G, B), the fourth byte is padding.
        var bgColor = new byte[] { data[offset + OffBgColor], data[offset + OffBgColor + 1], data[offset + OffBgColor + 2] };

        // LightSet: 3 × GsF_LIGHT = {s32 vx, s32 vy, s32 vz, u8 R, u8 G, u8 B, u8 pad, u32 pad}
        var lights = new BattleStageLightEntry[3];
        for (int i = 0; i < 3; i++)
        {
            int at = offset + OffLights + i * 0x10;
            int vx = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            int vy = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 4));
            int vz = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 8));
            lights[i] = new BattleStageLightEntry(vx, vy, vz, data[at + 12], data[at + 13], data[at + 14]);
        }

        // Ambient: 3 s32 values passed to SetBackColor.
        var ambient = new int[3];
        ambient[0] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + OffAmbient));
        ambient[1] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + OffAmbient + 4));
        ambient[2] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + OffAmbient + 8));

        return new BattleStageInfo(
            stageId,
            (int)(model >> 16), (int)(model & 0xFFFF),
            (int)(motions >> 16), (int)(motions & 0xFFFF),
            music, bgColor, lights, ambient);
    }
}
