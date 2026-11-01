using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// The fighter table file (<c>AAA/PRO/SMDLDATA.PRO</c>), which maps every battle fighter id to
/// the disc files holding its 3D model and motions.
/// </summary>
/// <remarks>
/// Layout (little-endian):
/// <code>
/// 0x00 u32  base         load address of the file in memory (pointers inside records are relative to it)
/// 0x04 u32  entries      file offset of the entry list
/// 0x08 u32  partners     file offset of the partner record table (stride 0xC4)
/// 0x0C u32  enemies      file offset of the enemy record table   (stride 0x48)
/// entry list: { s16 id; u8 index; u8 kind } repeated, terminated by id == 0
/// record (both tables):
///   +0x00 u32 model      file_id &lt;&lt; 16 | archive entry of the model descriptor
///   +0x04 u32 motions    file_id &lt;&lt; 16 | archive entry of the motion archive
/// </code>
/// A kind of 0x3A or more selects the enemy table, any other kind the partner table.
/// </remarks>
public sealed class FighterModelTable
{
    /// <summary>Size of one partner record in the fighter table file.</summary>
    public const int PartnerStride = 0xC4;

    /// <summary>Size of one enemy record in the fighter table file.</summary>
    public const int EnemyStride = 0x48;

    /// <summary>Smallest entry kind whose record lives in the enemy table.</summary>
    public const byte EnemyKindMinimum = 0x3A;

    private const int HeaderBytes = 16;
    private const int EntryBytes = 4;

    private readonly byte[] _data;
    private readonly int _partners;
    private readonly int _enemies;

    private FighterModelTable(byte[] data, IReadOnlyList<FighterModelEntry> entries, int partners, int enemies)
    {
        _data = data;
        Entries = entries;
        _partners = partners;
        _enemies = enemies;
    }

    /// <summary>Gets entries in file order, without the zero-id terminator.</summary>
    public IReadOnlyList<FighterModelEntry> Entries { get; }

    /// <summary>Parses the header and entry list of a fighter table file.</summary>
    /// <exception cref="FormatException">The file is shorter than its header or its offsets are invalid.</exception>
    public static FighterModelTable Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderBytes)
        {
            throw new FormatException($"Fighter table is {data.Length} bytes, shorter than its {HeaderBytes}-byte header.");
        }

        int entriesOffset = ReadOffset(data, 0x04, "entry list");
        int partners = ReadOffset(data, 0x08, "partner table");
        int enemies = ReadOffset(data, 0x0C, "enemy table");
        var entries = new List<FighterModelEntry>();
        for (int cursor = entriesOffset; cursor + EntryBytes <= data.Length; cursor += EntryBytes)
        {
            short id = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(cursor, 2));
            if (id == 0)
            {
                break;
            }

            entries.Add(new FighterModelEntry(id, data[cursor + 2], data[cursor + 3]));
        }

        return new FighterModelTable(data, entries, partners, enemies);
    }

    /// <summary>Resolves the first entry carrying <paramref name="fighterId"/> to its model and motion files.</summary>
    /// <exception cref="FormatException">The fighter is absent or its record lies outside the file.</exception>
    public FighterModelReference Resolve(int fighterId)
    {
        (FighterModelEntry entry, int record, bool isEnemy) = Locate(fighterId);
        uint model = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(record, 4));
        uint motions = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(record + 4, 4));
        return new FighterModelReference(
            fighterId, entry.Index, entry.Kind, isEnemy,
            (int)(model >> 16), (int)(model & 0xFFFF), (int)(motions >> 16), (int)(motions & 0xFFFF));
    }

    /// <summary>Reads placement, source cameras and face rectangles from the fighter record.</summary>
    public BattleFighterMetadata ReadMetadata(int fighterId)
    {
        (_, int record, bool isEnemy) = Locate(fighterId);
        int cameraCount = isEnemy ? 3 : 12;
        int cameraPos = record + 0x1A;
        int cameraRef = cameraPos + cameraCount * 6;
        int cameraProjection = cameraRef + cameraCount * 6;
        if (cameraProjection + cameraCount * 2 > _data.Length)
        {
            throw new FormatException($"Fighter {fighterId} camera arrays overrun the fighter table.");
        }

        var cameras = new List<BattleCamera>(cameraCount);
        for (int index = 0; index < cameraCount; index++)
        {
            cameras.Add(new BattleCamera(
                ReadI16(cameraPos + index * 6), ReadI16(cameraPos + index * 6 + 2), ReadI16(cameraPos + index * 6 + 4),
                ReadI16(cameraRef + index * 6), ReadI16(cameraRef + index * 6 + 2), ReadI16(cameraRef + index * 6 + 4),
                ReadI16(cameraProjection + index * 2)));
        }

        uint baseAddress = BinaryPrimitives.ReadUInt32LittleEndian(_data);
        uint faceAddress = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(record + 0x0C, 4));
        long faceOffsetLong = (long)faceAddress - baseAddress;
        if (faceOffsetLong < 0 || faceOffsetLong >= _data.Length)
        {
            throw new FormatException($"Fighter {fighterId} face offset 0x{faceOffsetLong:X} lies outside the fighter table.");
        }

        int faceOffset = (int)faceOffsetLong;
        var parts = new List<BattleFacePart>();
        bool terminated = false;
        for (int index = 0; index < 16; index++)
        {
            int at = faceOffset + index * 10;
            if (at + 10 > _data.Length)
            {
                throw new FormatException($"Fighter {fighterId} face part {index} overruns the fighter table.");
            }

            if (_data[at] == 0xFF) { terminated = true; break; }
            if (_data[at + 2] == 0)
            {
                continue;
            }

            parts.Add(new BattleFacePart(index, _data[at], _data[at + 1], _data[at + 2], _data[at + 3],
                [new(_data[at + 4], _data[at + 5]), new(_data[at + 6], _data[at + 7]), new(_data[at + 8], _data[at + 9])]));
        }

        if (!terminated)
        {
            throw new FormatException($"Fighter {fighterId} face has no x=0xff terminator in 16 parts.");
        }

        return new BattleFighterMetadata(cameras,
            new BattleGeometry(ReadI16(record + 0x10), ReadI16(record + 0x18)),
            new BattleFace(new BattleFaceSource(record, record + 0x0C, faceOffset, 10, "x==0xff"), parts));
    }

    private (FighterModelEntry Entry, int Record, bool IsEnemy) Locate(int fighterId)
    {
        FighterModelEntry entry = Entries.FirstOrDefault(candidate => candidate.FighterId == fighterId)
            ?? throw new FormatException($"Fighter {fighterId} is absent from the fighter table.");
        bool isEnemy = entry.Kind >= EnemyKindMinimum;
        long record = isEnemy ? _enemies + (long)entry.Index * EnemyStride : _partners + (long)entry.Index * PartnerStride;
        if (record + (isEnemy ? EnemyStride : PartnerStride) > _data.Length)
        {
            throw new FormatException($"{(isEnemy ? "Enemy" : "Partner")} record {entry.Index} of fighter {fighterId} lies outside the fighter table.");
        }

        return (entry, (int)record, isEnemy);
    }

    private short ReadI16(int at) => BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(at, 2));

    private static int ReadOffset(byte[] data, int at, string name)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        if (value > int.MaxValue)
        {
            throw new FormatException($"Fighter table {name} offset 0x{value:X} is invalid.");
        }

        return (int)value;
    }
}
