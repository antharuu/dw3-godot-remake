using System.Buffers.Binary;

namespace Dw3.Formats.Encounter;

/// <summary>
/// Decodes the DW3 EU encounter domain from FIELDSTG.PRO and WSTAG stage overlays.
///
/// FIELDSTG.PRO, loaded at <see cref="FieldOverlayBase"/>, contains enemy, encounter, and rate tables.
/// WSTAG overlays, loaded at <see cref="StageOverlayBase"/>, contain FieldBattles arrays.
/// </summary>
public static class FieldEncounterDecoder
{
    /// <summary>Overlay RAM address where FIELDSTG.PRO is loaded.</summary>
    public const uint FieldOverlayBase = 0x80082CB0u;

    /// <summary>Overlay address of FIELDSTG_enemies, which has 523 records of 12 bytes.</summary>
    public const uint EnemiesAddr = 0x80092DFCu;

    /// <summary>Number of BattleEnemy records in the enemies table.</summary>
    public const int EnemyCount = 523;

    /// <summary>Byte size of one BattleEnemy record.</summary>
    public const int EnemyStride = 12;

    /// <summary>Overlay address of FIELDSTG_encounters, which has 335 records of 28 bytes.</summary>
    public const uint EncountersAddr = 0x80094680u;

    /// <summary>Number of Encounter records in the encounters table.</summary>
    public const int EncounterCount = 335;

    /// <summary>Byte size of one Encounter record, including 2 bytes of alignment padding.</summary>
    public const int EncounterStride = 28;

    /// <summary>Overlay address of FIELDSTG_battleRates, which has 6 signed 32-bit values.</summary>
    public const uint BattleRatesAddr = 0x8009B6ACu;

    /// <summary>Number of step-rate values in the battle-rates table.</summary>
    public const int BattleRatesCount = 6;

    /// <summary>Overlay RAM address where every WSTAG stage overlay is loaded.</summary>
    public const uint StageOverlayBase = 0x800A5DE0u;

    /// <summary>Byte size of one AreaBattle record.</summary>
    public const int AreaBattleStride = 12;

    /// <summary>Byte size of one BattleList record.</summary>
    public const int BattleListStride = 36;

    /// <summary>Byte size of one FieldBattles record.</summary>
    public const int FieldBattlesStride = 28;

    /// <summary>RAM address of FIELDSTG_state, the global field-state structure.</summary>
    public const uint FieldStateAddress = 0x80099D80u;

    /// <summary>Byte offset of the battles pointer field within FIELDSTG_state.</summary>
    public const short BattlesFieldOffset = 0x20;

    /// <summary>
    /// Returns a project-owned neutral label for a music identifier read from disc.
    /// The encoded value is 0x60000000 | (bank &lt;&lt; 18) | number.
    /// </summary>
    /// <param name="music">Raw signed 32-bit music identifier.</param>
    /// <returns>The derived MUSIC(bank,number) label.</returns>
    public static string MusicLabel(int music)
    {
        uint value = unchecked((uint)music);
        int bank = (int)((value >> 18) & 0x3F);
        int number = (int)(value & 0x3FFFFu);
        return $"MUSIC({bank},{number})";
    }

    /// <summary>
    /// Decodes one BattleEnemy record from a 12-byte span at <paramref name="offset"/>.
    /// </summary>
    /// <param name="data">Buffer containing a BattleEnemy record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded enemy with its index left at zero.</returns>
    public static BattleEnemy DecodeBattleEnemy(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || checked(offset + EnemyStride) > data.Length)
        {
            throw new FormatException("BattleEnemy record lies outside the supplied buffer.");
        }

        int fighter = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        short level = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 4, 2));
        short hp = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 6, 2));
        short mp = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 8, 2));
        short strength = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 10, 2));
        return new BattleEnemy(0, fighter, level, hp, mp, strength);
    }

    /// <summary>
    /// Resolves a BattleEnemy overlay address to a zero-based index in the enemies table.
    /// </summary>
    /// <param name="address">Overlay address of a BattleEnemy record.</param>
    /// <returns>The zero-based enemy table index.</returns>
    /// <exception cref="FormatException">The address is not an aligned entry in FIELDSTG_enemies.</exception>
    public static int ResolveEnemyIndex(uint address)
    {
        long delta = (long)address - EnemiesAddr;
        if (delta < 0 || (delta % EnemyStride) != 0 || (delta / EnemyStride) >= EnemyCount)
        {
            throw new FormatException(
                $"Encounter enemy pointer 0x{address:X8} does not index FIELDSTG_enemies.");
        }

        return (int)(delta / EnemyStride);
    }

    /// <summary>
    /// Decodes all BattleEnemy records from the FIELDSTG.PRO data buffer.
    /// </summary>
    /// <param name="fieldStg">Raw FIELDSTG.PRO overlay bytes.</param>
    /// <returns>The indexed enemy records.</returns>
    public static IReadOnlyList<BattleEnemy> DecodeEnemies(ReadOnlySpan<byte> fieldStg)
    {
        int start = OverlayOffset(EnemiesAddr, FieldOverlayBase, fieldStg.Length, "FIELDSTG_enemies");
        var result = new BattleEnemy[EnemyCount];
        for (int index = 0; index < EnemyCount; index++)
        {
            BattleEnemy raw = DecodeBattleEnemy(fieldStg, start + (index * EnemyStride));
            result[index] = raw with { Index = index };
        }

        return result;
    }

    /// <summary>
    /// Decodes all Encounter records from FIELDSTG.PRO and resolves their enemy addresses.
    /// </summary>
    /// <param name="fieldStg">Raw FIELDSTG.PRO overlay bytes.</param>
    /// <param name="enemies">The decoded enemy table referenced by each encounter.</param>
    /// <returns>The indexed encounter records.</returns>
    public static IReadOnlyList<EncounterRecord> DecodeEncounters(
        ReadOnlySpan<byte> fieldStg,
        IReadOnlyList<BattleEnemy> enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        int start = OverlayOffset(EncountersAddr, FieldOverlayBase, fieldStg.Length, "FIELDSTG_encounters");
        var result = new EncounterRecord[EncounterCount];
        for (int index = 0; index < EncounterCount; index++)
        {
            int offset = start + (index * EncounterStride);
            if ((offset + EncounterStride) > fieldStg.Length)
            {
                throw new FormatException("Encounter record lies outside FIELDSTG.PRO.");
            }

            uint firstAddress = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(offset, 4));
            uint secondAddress = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(offset + 4, 4));
            uint thirdAddress = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(offset + 8, 4));
            int[] indices = [ResolveEnemyIndex(firstAddress), ResolveEnemyIndex(secondAddress), ResolveEnemyIndex(thirdAddress)];
            byte ambush = fieldStg[offset + 12];
            byte kind = fieldStg[offset + 13];
            byte[] blocks = fieldStg.Slice(offset + 14, 12).ToArray();
            var slots = indices
                .Select(enemyIndex => new EncounterEnemySlot(enemyIndex, enemies[enemyIndex]))
                .ToList();
            result[index] = new EncounterRecord(index, indices, slots, ambush, kind, blocks);
        }

        return result;
    }

    /// <summary>
    /// Decodes the battle-rate step values from the FIELDSTG.PRO data buffer.
    /// </summary>
    /// <param name="fieldStg">Raw FIELDSTG.PRO overlay bytes.</param>
    /// <returns>The six signed step-rate values.</returns>
    public static IReadOnlyList<int> DecodeBattleRates(ReadOnlySpan<byte> fieldStg)
    {
        int start = OverlayOffset(BattleRatesAddr, FieldOverlayBase, fieldStg.Length, "FIELDSTG_battleRates");
        var result = new int[BattleRatesCount];
        for (int index = 0; index < BattleRatesCount; index++)
        {
            result[index] = BinaryPrimitives.ReadInt32LittleEndian(fieldStg.Slice(start + (index * 4), 4));
        }

        return result;
    }

    /// <summary>
    /// Decodes one AreaBattle record from a stage overlay address.
    /// </summary>
    /// <param name="overlay">Raw stage-overlay bytes.</param>
    /// <param name="address">RAM address of the AreaBattle record.</param>
    /// <param name="overlayBase">RAM load address of <paramref name="overlay"/>.</param>
    /// <returns>The decoded area battle.</returns>
    public static AreaBattle DecodeAreaBattle(ReadOnlySpan<byte> overlay, uint address, uint overlayBase)
    {
        int offset = OverlayOffset(address, overlayBase, overlay.Length, "AreaBattle");
        if ((offset + AreaBattleStride) > overlay.Length)
        {
            throw new FormatException($"AreaBattle record lies outside overlay at 0x{address:X8}.");
        }

        int encounter = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
        int stage = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 4, 4));
        int music = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4));
        return new AreaBattle(encounter, stage, music, MusicLabel(music));
    }

    /// <summary>
    /// Decodes one BattleList record from a stage overlay address.
    /// </summary>
    /// <param name="overlay">Raw stage-overlay bytes.</param>
    /// <param name="address">RAM address of the BattleList record.</param>
    /// <param name="overlayBase">RAM load address of <paramref name="overlay"/>.</param>
    /// <returns>The decoded battle list.</returns>
    public static BattleList DecodeBattleList(ReadOnlySpan<byte> overlay, uint address, uint overlayBase)
    {
        int offset = OverlayOffset(address, overlayBase, overlay.Length, "BattleList");
        if ((offset + BattleListStride) > overlay.Length)
        {
            throw new FormatException($"BattleList record lies outside overlay at 0x{address:X8}.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
        if (count < 0 || count >= BattleRatesCount)
        {
            throw new FormatException($"BattleList count {count} is not a valid battle-rate index (0..{BattleRatesCount - 1}).");
        }

        var battles = new List<AreaBattle>(8);
        for (int index = 0; index < 8; index++)
        {
            uint battleAddress = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 4 + (index * 4), 4));
            battles.Add(DecodeAreaBattle(overlay, battleAddress, overlayBase));
        }

        return new BattleList(count, battles);
    }

    /// <summary>
    /// Decodes one FieldBattles record from a stage overlay address.
    /// </summary>
    /// <param name="overlay">Raw stage-overlay bytes.</param>
    /// <param name="address">RAM address of the FieldBattles record.</param>
    /// <param name="overlayBase">RAM load address of <paramref name="overlay"/>.</param>
    /// <returns>The decoded field battle record.</returns>
    public static FieldBattles DecodeFieldBattles(ReadOnlySpan<byte> overlay, uint address, uint overlayBase)
    {
        int offset = OverlayOffset(address, overlayBase, overlay.Length, "FieldBattles");
        if ((offset + FieldBattlesStride) > overlay.Length)
        {
            throw new FormatException($"FieldBattles record lies outside overlay at 0x{address:X8}.");
        }

        int serial = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
        int id = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 4, 4));
        int pad8 = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4));
        var areas = new List<BattleList>(4);
        for (int index = 0; index < 4; index++)
        {
            uint listAddress = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 12 + (index * 4), 4));
            areas.Add(DecodeBattleList(overlay, listAddress, overlayBase));
        }

        return new FieldBattles(serial, id, pad8, areas);
    }

    /// <summary>
    /// Converts an overlay RAM address to a checked byte offset.
    /// </summary>
    /// <param name="address">RAM address to convert.</param>
    /// <param name="overlayBase">RAM load address of the overlay.</param>
    /// <param name="size">Overlay byte length.</param>
    /// <param name="description">Human-readable name used in a failure message.</param>
    /// <returns>The checked offset within the overlay.</returns>
    private static int OverlayOffset(uint address, uint overlayBase, int size, string description)
    {
        long delta = (long)address - overlayBase;
        if (delta < 0 || delta >= size)
        {
            throw new FormatException($"{description} address 0x{address:X8} is outside overlay (base 0x{overlayBase:X8}).");
        }

        return (int)delta;
    }
}
